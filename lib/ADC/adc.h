#include "avr/io.h"

#ifndef AtoD
#define AtoD

typedef enum ADC_Channel
{
    ADC0 = 0x00,
    ADC1 = 0x01,
    ADC2 = 0x02,
    ADC3 = 0x03,
    ADC4 = 0x04,
    ADC5 = 0x05,
    ADC6 = 0x06,
    ADC7 = 0x07,
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