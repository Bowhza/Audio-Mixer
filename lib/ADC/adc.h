#include "avr/io.h"

#ifndef AtoD
#define AtoD

typedef enum ADC_Channel
{
    ADC0 = 0b00000000,
    ADC1 = 0b00000001,
    ADC2 = 0b00000010,
    ADC3 = 0b00000011,
    ADC4 = 0b00000100,
    ADC5 = 0b00000101,
    ADC6 = 0b00000110,
    ADC7 = 0b00000111,
} ADC_Channel;

/// @brief Enum for Interrupts Enable
typedef enum ADC_Interrupt
{
    IDIS,
    IEN
} ADC_Interrupt;

/// @brief Initialize the ADC
/// @param Interrupts IEN to enable interrupts or IDIS to dissable interrupts
void ADC_Init(ADC_Interrupt Interrupts);

/// @brief Set the current pin for the ADC
/// @param ADC_Channel ADC0 - ADC7
void ADC_SetChannel(ADC_Channel ADC_Channel);

#endif