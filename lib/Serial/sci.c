#include "sci.h"

void Serial_Init()
{
    // Set the baud rate
    // USART Baud High (0b XXXX ----)
    UBRR0H = (MYUBRR >> 8);
    // USART Baud Low (0b ---- ----)
    UBRR0L = (unsigned char)MYUBRR;

    // Enable receiver and transmitter
    UCSR0B = (1 << RXEN0) | (1 << TXEN0);
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
    if (UCSR0A & (1 << RXC0))
    {
        *data = UDR0;
        return 0;
    }
    return 1;
}