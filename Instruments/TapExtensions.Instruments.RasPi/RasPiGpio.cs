﻿using System;
using TapExtensions.Interfaces.Gpio;

namespace TapExtensions.Instruments.RasPi
{
    public class RasPiGpio : RasPiResource, IGpio
    {
        public void SetPinState(int pin, EPinState state)
        {
            Connect();
            try
            {
                // ToDo:
                /*
                    /sys/class/gpio/gpio11/direction
                    /sys/class/gpio/gpio11/value
                    /dev/gpiochipN
                    sudo usermod -a -G gpio <username>
                */
            }
            finally
            {
                Disconnect();
            }
        }

        public EPinState GetPinState(int pin)
        {
            throw new NotImplementedException();
        }
    }
}