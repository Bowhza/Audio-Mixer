#include "adc.h"

void ADC_Init()
{
    // AREF, Internal VRef turned OFF
    // Left adjust the results
    ADMUX = 0b01000000;

    // Enable ADC
    // Start Conversion
    // Auto Trigger
    // 128 Prescaler
    ADCSRA = 0b11100111;

    ADCSRB = 0b00000000;
}

void ADC_SetChannel(ADC_Channel Channel)
{
    // Clear previous channel selection and set the new channel
    ADMUX = (ADMUX & 0xF0) | (Channel & 0x0F);
}