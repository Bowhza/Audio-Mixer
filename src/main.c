#include "avr/io.h"
#include "util/delay.h"
#include "avr/interrupt.h"
#include "avr/eeprom.h"
#include "avr/pgmspace.h"
#include "stdio.h"
#include "stdlib.h"
#include "string.h"
#include "sci.h"
#include "adc.h"

// Reference Voltage for the ADC
#define REFERENCE_VOLTAGE 5.0
// Default value is 1024.0
#define ADC_MULTIPLIER 1024.0
// Default fader count (used the first time, before anything is saved to EEPROM)
#define DEFAULT_FADER_COUNT 5
// Hardware limits: the ATmega328P exposes ADC0..ADC7.
#define MIN_FADER_COUNT 1
#define MAX_FADER_COUNT 8
// Default hysteresis threshold (in ADC counts) to suppress jitter when a slider
// is idle. Runtime-adjustable over serial and persisted to EEPROM.
#define DEFAULT_HYSTERESIS 3
#define MIN_HYSTERESIS 1
#define MAX_HYSTERESIS 64

// Firmware / protocol identity. The driver matches on the constant SIGNATURE
// (never user-editable) so auto-detection keeps working even after a rename.
// The DEVICE name below is just a friendly label the user can change.
#define FW_VERSION "2.0.2"
#define FW_PROTOCOL 1
#define FW_SIGNATURE "AudioMixer"
#define FW_DEFAULT_NAME "AudioMixer"

// Version tag embedded in the flash image. The desktop app scans the bundled
// firmware.hex for "AMFW-VERSION:" to learn which version it can flash, and
// compares that with the version the connected device reports.
// PROGMEM keeps it out of RAM. "used" stops the linker from discarding it.
const char FirmwareTag[] PROGMEM __attribute__((used)) = "AMFW-VERSION:" FW_VERSION;

// Maximum device-name length (excluding the null terminator).
#define NAME_MAX_LEN 15

// Size of the incoming command line buffer (fits "NAME=<15 chars>").
#define RX_BUFFER_SIZE 24

// EEPROM layout: a magic byte marks that valid settings have been stored, so a
// fresh chip falls back to the defaults instead of reading 0xFF.
#define EE_MAGIC 0xA6
uint8_t EEMEM ee_magic;
uint8_t EEMEM ee_fader_count;
char EEMEM ee_name[NAME_MAX_LEN + 1];
// Declared last so existing stored name/count keep their EEPROM addresses.
uint8_t EEMEM ee_hysteresis;

// String to transmit over serial
char ADCStr[128] = "";

// Array of ADC Values (sized for the hardware maximum).
volatile int ADC_Values[MAX_FADER_COUNT];

// Active settings, loaded from EEPROM at boot and changeable at runtime.
uint8_t FaderCount = DEFAULT_FADER_COUNT;
char DeviceName[NAME_MAX_LEN + 1] = FW_DEFAULT_NAME;
uint8_t Hysteresis = DEFAULT_HYSTERESIS;

// Current selected ADC Channel
ADC_Channel Channel;

// Incoming serial command buffer and its write index.
char RxBuffer[RX_BUFFER_SIZE];
unsigned char RxIndex = 0;

// Copies src into DeviceName, keeping only JSON-safe printable ASCII and
// capping the length. Falls back to the default if nothing usable remains.
void SanitizeName(const char *src)
{
    unsigned char j = 0;
    for (unsigned char i = 0; src[i] != '\0' && j < NAME_MAX_LEN; i++)
    {
        char c = src[i];
        if (c >= 0x20 && c <= 0x7E && c != '"' && c != '\\')
            DeviceName[j++] = c;
    }
    DeviceName[j] = '\0';

    if (j == 0)
        strcpy(DeviceName, FW_DEFAULT_NAME);
}

// Loads saved settings (fader count + device name) from EEPROM, or keeps the
// defaults when nothing valid is stored.
void LoadSettings(void)
{
    if (eeprom_read_byte(&ee_magic) != EE_MAGIC)
        return;

    uint8_t n = eeprom_read_byte(&ee_fader_count);
    if (n >= MIN_FADER_COUNT && n <= MAX_FADER_COUNT)
        FaderCount = n;

    char buffer[NAME_MAX_LEN + 1];
    eeprom_read_block(buffer, ee_name, NAME_MAX_LEN + 1);
    buffer[NAME_MAX_LEN] = '\0'; // guarantee termination
    SanitizeName(buffer);

    // Added in a later firmware. Older EEPROM reads 0xFF here, so fall back.
    uint8_t h = eeprom_read_byte(&ee_hysteresis);
    if (h >= MIN_HYSTERESIS && h <= MAX_HYSTERESIS)
        Hysteresis = h;
}

// Persists a new fader count to EEPROM (clamped to the hardware range).
void SaveFaderCount(uint8_t n)
{
    if (n < MIN_FADER_COUNT)
        n = MIN_FADER_COUNT;
    if (n > MAX_FADER_COUNT)
        n = MAX_FADER_COUNT;

    FaderCount = n;
    eeprom_update_byte(&ee_fader_count, n);
    eeprom_update_byte(&ee_magic, EE_MAGIC);

    // Keep the scan index in range if the count shrank, and point the ADC mux
    // at it so the next reading is not attributed to the wrong channel.
    if (Channel > FaderCount - 1)
    {
        Channel = 0;
        ADC_SetChannel(Channel);
    }
}

// Parses a small unsigned decimal number. Returns -1 when the text is empty or
// contains anything but digits, so a corrupted command is ignored rather than
// being clamped into a "valid" but wrong value.
int ParseNumber(const char *s)
{
    int value = 0;
    if (*s == '\0')
        return -1;
    for (; *s != '\0'; s++)
    {
        if (*s < '0' || *s > '9')
            return -1;
        value = value * 10 + (*s - '0');
        if (value > 255)
            return -1;
    }
    return value;
}

// Persists a new device name to EEPROM.
void SaveName(const char *name)
{
    SanitizeName(name);
    eeprom_update_block(DeviceName, ee_name, NAME_MAX_LEN + 1);
    eeprom_update_byte(&ee_magic, EE_MAGIC);
}

// Persists a new hysteresis threshold to EEPROM (clamped to a sensible range).
void SaveHysteresis(uint8_t n)
{
    if (n < MIN_HYSTERESIS)
        n = MIN_HYSTERESIS;
    if (n > MAX_HYSTERESIS)
        n = MAX_HYSTERESIS;

    Hysteresis = n;
    eeprom_update_byte(&ee_hysteresis, n);
    eeprom_update_byte(&ee_magic, EE_MAGIC);
}

// Sends the identify/handshake response so the driver can recognize the device.
void SendIdentify(void)
{
    // avr-libc's %S means "string in program memory". GCC assumes the standard
    // wchar_t meaning and warns, so silence that one check here.
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wformat"
    sprintf(ADCStr,
            "{\"Device\": \"%s\", \"Signature\": \"%s\", \"Protocol\": %d, \"Faders\": %d, \"Hysteresis\": %d, \"Firmware\": \"%S\"}\n",
            DeviceName, FW_SIGNATURE, FW_PROTOCOL, FaderCount, Hysteresis,
            // %S prints a program-memory string: the version part of FirmwareTag.
            // Reading it here also keeps the tag in the image for the app to find.
            FirmwareTag + (sizeof("AMFW-VERSION:") - 1));
#pragma GCC diagnostic pop
    Serial_Tx(ADCStr);
}

// Transmits the latest stored reading for a single channel, regardless of
// hysteresis. Used by the normal loop (on change) and by SYNC (send everything).
void SendChannel(unsigned char channel)
{
    // Convert ADC value to voltage
    float voltage = ((float)ADC_Values[channel] / ADC_MULTIPLIER) * REFERENCE_VOLTAGE;

    // Transfer function for converting Voltage to Percentage
    float percentage = ((REFERENCE_VOLTAGE - voltage) / (REFERENCE_VOLTAGE)) * 100.0;

    sprintf(ADCStr, "{\"Channel\": %d, \"Voltage\": %.2f, \"Percentage\": %.1f}\n",
            channel, voltage, percentage);
    Serial_Tx(ADCStr);
}

// Sends the current state of every fader. The driver requests this on connect
// (command "SYNC") so the UI reflects the real slider positions immediately,
// even when the board did not reset when the port was opened.
void SendAllChannels(void)
{
    for (unsigned char i = 0; i < FaderCount; i++)
        SendChannel(i);
}

void HandleByte(unsigned char byte);

// Polls the serial port for an incoming command line (non-blocking) and acts on
// it once a full line is received. Handles "ID?", "SYNC", "FADERS=n", "NAME=x".
void PollCommands(void)
{
    unsigned char byte;

    // Serial_RxByte returns 0 when a byte was read, 1 when nothing is waiting.
    // Drain everything the RX interrupt has buffered so far.
    while (Serial_RxByte(&byte) == 0)
        HandleByte(byte);
}

// Accumulates one received byte and dispatches a command on end of line.
void HandleByte(unsigned char byte)
{
    // End of a command line: terminate, dispatch, and reset the buffer.
    if (byte == '\n' || byte == '\r')
    {
        RxBuffer[RxIndex] = '\0';

        if (strcmp(RxBuffer, "ID?") == 0)
            SendIdentify();
        else if (strcmp(RxBuffer, "SYNC") == 0)
            SendAllChannels();
        else if (strncmp(RxBuffer, "FADERS=", 7) == 0)
        {
            int n = ParseNumber(RxBuffer + 7);
            if (n >= MIN_FADER_COUNT && n <= MAX_FADER_COUNT)
                SaveFaderCount((uint8_t)n);
            // Always reply so the driver can confirm what is actually stored.
            SendIdentify();
        }
        else if (strncmp(RxBuffer, "NAME=", 5) == 0)
        {
            SaveName(RxBuffer + 5);
            SendIdentify();
        }
        else if (strncmp(RxBuffer, "HYST=", 5) == 0)
        {
            int n = ParseNumber(RxBuffer + 5);
            if (n >= 0)
                SaveHysteresis((uint8_t)n);
            SendIdentify();
        }

        RxIndex = 0;
        return;
    }

    // Accumulate the byte, guarding against overflow (discard the line on overrun).
    if (RxIndex < RX_BUFFER_SIZE - 1)
        RxBuffer[RxIndex++] = byte;
    else
        RxIndex = 0;
}

int main()
{
    // Restore saved settings before anything else.
    LoadSettings();

    // Initialize Serial Communication
    Serial_Init();
    // Enable interrupts (the USART receive interrupt buffers incoming commands)
    sei();
    // Initialize Analog to Digital Converter
    ADC_Init(IDIS);

    // Announce ourselves once on boot so a driver that is already listening can
    // detect the device immediately without having to send a query first.
    SendIdentify();

    // Start every stored reading far outside the ADC range (0..1023) so the first
    // pass of the loop always transmits each fader's real position. Otherwise a
    // slider sitting within the hysteresis of 0 is never sent until it jitters.
    for (unsigned char i = 0; i < MAX_FADER_COUNT; i++)
        ADC_Values[i] = -2048;

    // Main Loop
    while (1)
    {
        // Answer any pending handshake/command from the driver.
        PollCommands();

        // Read the ADC value
        int ADC_Value = ADC;

        // Check if the stored value for the channel different
        if (abs(ADC_Value - ADC_Values[Channel]) >= Hysteresis)
        {
            // Store the new ADC Value and transmit it
            ADC_Values[Channel] = ADC_Value;
            SendChannel(Channel);
        }

        // Update the channel
        if (++Channel > FaderCount - 1)
            Channel = 0;

        // Set the ADC Channel
        ADC_SetChannel(Channel);
    }
}
