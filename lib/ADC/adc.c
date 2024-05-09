#include "adc.h"
#include "util/delay.h"

void ADC_Init(ADC_Interrupt Interrupts)
{
    // AVcc with external capacitor at AREF pin
    ADMUX = (1 << REFS0);

    // Enable interrupts if specified
    if (Interrupts)
        ADCSRA |= (1 << ADIE); // Set ADIE bit to enable ADC interrupt
    else
        ADCSRA &= ~(1 << ADIE); // Clear ADIE bit to disable ADC interrupt

    // Set left adjustment result if necessary
    // ADMUX &= ~(1 << ADLAR); // Uncomment this line if you want right adjustment result

    // Enable ADC, start conversion, auto trigger, 128 prescaler
    ADCSRA |= (1 << ADEN) | (1 << ADSC) | (1 << ADATE) | (1 << ADPS2) | (1 << ADPS1) | (1 << ADPS0);

    // Clear ADCSRB to set free running mode
    ADCSRB = 0;
}

void ADC_SetChannel(ADC_Channel Channel)
{
    // Clear previous channel selection and set the new channel
    ADMUX = (ADMUX & 0xF0) | (Channel & 0x0F);
    // Necessary delay when switching between channels to prevent desync
    _delay_ms(1);
}