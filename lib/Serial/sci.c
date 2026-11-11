#include "sci.h"
#include "avr/interrupt.h"

// Interrupt-driven receive ring buffer. The USART hardware only holds ~2 bytes,
// and the main loop spends long stretches busy-waiting in Serial_Tx, so polling
// RXC0 from the loop drops command bytes. The ISR captures every byte instead.
#define RX_RING_SIZE 64 // power of two
#define RX_RING_MASK (RX_RING_SIZE - 1)

static volatile unsigned char RxRing[RX_RING_SIZE];
static volatile unsigned char RxHead = 0; // written by ISR
static volatile unsigned char RxTail = 0; // written by main loop

ISR(USART_RX_vect)
{
    unsigned char data = UDR0;
    unsigned char next = (RxHead + 1) & RX_RING_MASK;

    // Drop the byte if the buffer is full (never overwrite unread data).
    if (next != RxTail)
    {
        RxRing[RxHead] = data;
        RxHead = next;
    }
}

void Serial_Init()
{
    // Set the baud rate
    // USART Baud High (0b XXXX ----)
    UBRR0H = (MYUBRR >> 8);
    // USART Baud Low (0b ---- ----)
    UBRR0L = (unsigned char)MYUBRR;

    // Enable receiver, transmitter and the receive-complete interrupt
    UCSR0B = (1 << RXEN0) | (1 << TXEN0) | (1 << RXCIE0);
    // Set frame format to 8 data bits, 1 stop bit
    UCSR0C = 0b00000110;
}

void Serial_TxByte(unsigned char data)
{
    while (!(UCSR0A & (1 << UDRE0)))
        ;

    UDR0 = data;
}

void Serial_Tx(char *string)
{
    // Transmits every char in a string
    while (*string)
    {
        Serial_TxByte(*string);
        string++;
    }
}

int Serial_RxByte(unsigned char *data)
{
    if (RxTail == RxHead)
        return 1;

    *data = RxRing[RxTail];
    RxTail = (RxTail + 1) & RX_RING_MASK;
    return 0;
}
