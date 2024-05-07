#include "avr/io.h"
#include "util/delay.h"
#include "stdio.h"
#include "stdlib.h"
#include "sci.h"
#include "adc.h"

char ADCStr[20] = "";

// Array of ADC Values
int ADC_Values[5];
ADC_Channel Channel;

int main()
{
    Serial_Init();
    ADC_Init(ADC0);

    while (1)
    {
        _delay_ms(1500);

        for (Channel = ADC0; Channel < ADC5; Channel++)
        {
            ADC_SetChannel(Channel);

            ADC_Values[Channel] = ADC;

            // Print raw ADC value for debugging
            sprintf(ADCStr, "ADC[%d]: %u | ", Channel, ADC_Values[Channel]);
            Serial_Tx(ADCStr);

            // Convert ADC value to voltage
            float voltage = ((float)ADC_Values[Channel] / 1024.0) * 5.0;

            // Format the voltage value into a string
            sprintf(ADCStr, "Voltage: %.2fV | ", voltage);
            Serial_Tx(ADCStr);

            // Transfer function for converting Voltage to Percentage
            float percentage = ((5.0 - voltage) / (5.0)) * 100.0;

            // Format percentage into a string
            sprintf(ADCStr, "Percentage: %.1f%%\n", percentage);
            Serial_Tx(ADCStr);
        }
        Serial_Tx("-----------------------------------------------\n");
    }
}