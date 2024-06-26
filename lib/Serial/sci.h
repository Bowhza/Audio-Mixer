#include "avr/io.h"

#ifndef SCI
#define SCI

#define CLOCK_SPEED 16000000
#define BAUD 9600
#define MYUBRR ((((CLOCK_SPEED * 10 / 16) / BAUD) + 5) / 10) - 1

/// @brief Initializes the USART
/// using 8 data bits and 1 stop bit.
void Serial_Init();

/// @brief Transmit a byte over serial
/// @param data byte to transmit
void Serial_TxByte(unsigned char data);

/// @brief Transmits a string over USART
/// @param string String to transmit
void Serial_Tx(char *string);

/// @brief Receive byte over serial
/// @param data received byte
/// @return if byte is received
int Serial_RxByte(unsigned char *data);

#endif