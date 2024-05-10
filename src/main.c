#include "avr/io.h"
#include "util/delay.h"
#include "avr/interrupt.h"
#include "stdio.h"
#include "stdlib.h"
#include "sci.h"
#include "adc.h"

// Reference Voltage for the ADC
#define REFERENCE_VOLTAGE 5.0
// Default value is 1024.0
#define ADC_MULTIPLIER 1024.0
// Fader Potentiometer Count
#define FADER_COUNT 5

// String to transmit over serial
char ADCStr[100] = "";

// Array of ADC Values
volatile int ADC_Values[FADER_COUNT];

// Flag when the ADC Interrupt Triggers
volatile char ADC_Read = 0;

// Current selected ADC Channel
ADC_Channel Channel;

int main()
{
    // Enable Interrupts
    // Currently not required
    // sei();

    // Initialize Serial Communication
    Serial_Init();
    // Initialize Analog to Digital Converter
    ADC_Init(IDIS);

    // Main Loop
    while (1)
    {
        // Read the ADC value
        ADC_Values[Channel] = ADC;

        // Convert ADC value to voltage
        float voltage = ((float)ADC_Values[Channel] / ADC_MULTIPLIER) * REFERENCE_VOLTAGE;

        // Transfer function for converting Voltage to Percentage
        float percentage = ((REFERENCE_VOLTAGE - voltage) / (REFERENCE_VOLTAGE)) * 100.0;

        // Format RAW ADC, Voltage, and calculated slider percentage into a string
        sprintf(ADCStr, "{\"Channel\": %d, \"Voltage\": %.2f, \"Percentage\": %.1f}\n",
                Channel,
                voltage,
                percentage);

        // Transmit the string over Serial
        Serial_Tx(ADCStr);

        // Update the channel
        if (++Channel > FADER_COUNT - 1)
        {
            Channel = 0;
        }

        // Set the ADC Channel
        ADC_SetChannel(Channel);
    }
}