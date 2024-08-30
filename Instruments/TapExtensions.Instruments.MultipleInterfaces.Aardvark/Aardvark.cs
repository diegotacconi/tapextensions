﻿using System;
using OpenTap;

namespace TapExtensions.Instruments.MultipleInterfaces.Aardvark
{
    [Display("TotalPhase Aardvark",
        Groups: new[] { "TapExtensions", "Instruments", "MultipleInterfaces" },
        Description: "TotalPhase Aardvark I2C/SPI Host Adapter")]
    public partial class Aardvark : Instrument
    {
        #region Settings

        [Display("Device Number", Order: 1)] public int DevNumber { get; set; }

        [Display("Config Mode", Order: 2)] public AardvarkConfig ConfigMode { get; set; }

        public enum ETargetPower
        {
            Off,
            [Display("5 Volts")] On5V0
        }

        [Display("Target Power", Order: 3, Description: "(Pin 4, 6)")]
        public ETargetPower TargetPower { get; set; }

        public enum EPullupResistors
        {
            Off,
            On
        }

        [Display("I2C Pull-ups", Order: 4, Description: "Set I2C pull-up resistors state to on/off")]
        public EPullupResistors PullupResistors { get; set; }

        // ReSharper disable once InconsistentNaming
        public enum EI2cBitrate
        {
            [Display("100 KHz")] OneHundred = 100,
            [Display("200 KHz")] TwoHundred = 200,
            [Display("300 KHz")] ThreeHundred = 300,
            [Display("400 KHz")] FourHundred = 400,
            [Display("500 KHz")] FiveHundred = 500,
            [Display("600 KHz")] SixHundred = 600,
            [Display("700 KHz")] SevenHundred = 700,
            [Display("800 KHz")] EightHundred = 800
        }

        [Display("I2C Bus bit rate", Order: 5)]
        // ReSharper disable once InconsistentNaming
        public EI2cBitrate I2cBitRateKhz { get; set; }
        // public int I2cBitRateKhz { get; set; }

        #endregion

        private readonly object _instLock = new object();
        internal int AardvarkHandle;
        private bool _isInitialized;

        // The Aardvark adapter SPI master can operate at bitrates of 125 kHz, 250 Khz, 500 Khz, 1 Mhz, 2 MHz, 4 Mhz, and 8 Mhz.
        public readonly int[] ArdvarkSpiMasterClockFreqsKhz = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        // TotalPhase/aardvark-v5.15.pdf/Chapter5.5.1/4
        // It is not possible to receive messages larger than approximately 4 KiB as a slave
        // due to operating system limitations on the asynchronous incoming buffer. As such,
        // one should not queue up more than 4 KiB of total slave data between calls to the Aardvark API.
        private const ushort MaxTxRxBytes = 4000;

        public Aardvark()
        {
            // Default values
            Name = nameof(Aardvark);
            ConfigMode = AardvarkConfig.AA_CONFIG_SPI_I2C;
            // I2cBitRateKhz = 100;
            I2cBitRateKhz = EI2cBitrate.OneHundred;
            TargetPower = ETargetPower.Off;
            PullupResistors = EPullupResistors.Off;
        }

        public override void Open()
        {
            base.Open();

            // ToDo: net_aa_configure(aardvark, config);
            // ToDo: net_aa_version(aardvark, ref version);
            // ToDo: return Marshal.PtrToStringAnsi(net_aa_status_string(status));
            // ToDo: return net_aa_unique_id(aardvark);
            // ToDo: return net_aa_port(aardvark);

            lock (_instLock)
            {
                if (_isInitialized)
                    throw new ApplicationException("Aardvark(s) already initialized!");

                // Find Devices
                var maxNumDevices = 16;
                var devices = new ushort[maxNumDevices];
                var numDevicesFound = AardvarkWrapper.aa_find_devices(maxNumDevices, devices);

                if (numDevicesFound < 1)
                    throw new ApplicationException("Number of Aardvark devices found should be more than 0.");

                Log.Debug("Found " + numDevicesFound + " Aardvarks. Initializing device number " + DevNumber + ".");

                // Open an Aardvark device
                const int stepDelay = 333;
                var tryDelay = 0;
                var aExt = new AardvarkWrapper.AardvarkExt();
                do
                {
                    TapThread.Sleep(tryDelay);
                    tryDelay += stepDelay; // 0, 333, 666, 999...
                    AardvarkHandle = AardvarkWrapper.aa_open_ext(DevNumber, ref aExt);
                } while (AardvarkHandle < 0 && tryDelay < 1000);

                if (AardvarkHandle < 0)
                {
                    var errorMsg = AardvarkWrapper.aa_status_string(AardvarkHandle);
                    throw new ApplicationException($"{Name}: Error {AardvarkHandle}, {errorMsg}");
                }

                // Get Unique ID
                var uniqueId = AardvarkWrapper.aa_unique_id(AardvarkHandle);
                if (uniqueId < 1)
                    throw new ApplicationException("Aardvark's unique ID should be non-zero if valid.");

                Log.Debug("Aardvark<" + DevNumber + "> has serial number of " + uniqueId + ".");


                // Configure
                _isInitialized = true;

                try
                {
                    var stat = AardvarkWrapper.aa_configure(AardvarkHandle, ConfigMode);
                    if (stat != (int)ConfigMode)
                        throw new ApplicationException("ERRor[" + stat +
                                                       "] when configuring aardvark to SPI+I2C mode.");

                    // Settings below must add, if some application stops working. SpiInit set these, but if application do not run it???
                    //stat = AardvarkWrapper.net_aa_spi_configure(AardvarkHandle, AardvarkSpiPolarity.AaSpiPolRisingFalling,
                    //                       AardvarkSpiPhase.AaSpiPhaseSampleSetup, AardvarkSpiBitorder.AaSpiBitorderMsb);
                    //if (stat != (int)AardvarkStatus.AA_OK)
                    //    throw new ApplicationException("SPI initial spi_configure return: " + stat);
                }
                catch (Exception)
                {
                    _isInitialized = false;
                    throw;
                }


                SetTargetPower(TargetPower);

                SetPullUpState(PullupResistors);

                // TODO: net_aa_version(aardvark, ref version);

                // Status
                //var status = AardvarkStatus.AaUnableToOpen;
                //var statusString = Marshal.PtrToStringAnsi(AardvarkWrapper.net_aa_status_string((int)status));
                //Log.Debug("Aardvark<" + DevNumber + "> status is :" + statusString + ".");

                Log.Debug("Aardvark<" + DevNumber + "> initialized with try " + tryDelay / stepDelay + ".");
            }
        }

        public override void Close()
        {
            CheckIfInitialized();
            AardvarkWrapper.aa_close(AardvarkHandle);
            AardvarkHandle = 0;
            _isInitialized = false;
            base.Close();
        }

        protected void CheckIfInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException($"{Name} not initialized");
        }

        private void LogDebugData(string infoStart, byte[] data, int stat = 0)
        {
            if (string.IsNullOrEmpty(infoStart))
                throw new ArgumentException("LogDebugData Value(infoStart) cannot be null or empty.",
                    nameof(infoStart));

            if (data == null)
            {
                Log.Debug(infoStart + " have NOT data(=null). Status: " + stat);
                return;
            }

            CheckIfInitialized();
            lock (_instLock)
            {
                var dataLen = data.Length;
                const int maxLen = 100;
                var hexValues = "0x( ";

                for (var i = 0; i < dataLen; i++)
                {
                    if (i > 0)
                        hexValues += " ";

                    hexValues += data[i].ToString("X2");

                    if (hexValues.Length > maxLen)
                    {
                        // Shorten long logs
                        hexValues += " ---";
                        break;
                    }
                }

                // All Aardvark API functions return an integer which is either the result of the
                // transaction, or a status code if negative.
                if (stat < 0)
                    Log.Debug(infoStart + hexValues + " ). Status: " + stat);
                else
                    Log.Debug(infoStart + hexValues + " )");
            }
        }

        public void SetTargetPower(ETargetPower target)
        {
            lock (_instLock)
            {
                CheckIfInitialized();
                Log.Debug($"Setting target power to {target}");
                int status;
                switch (target)
                {
                    case ETargetPower.Off:
                        status = AardvarkWrapper.aa_target_power(AardvarkHandle, 0x00);
                        if (status == 0x00) return;
                        break;
                    case ETargetPower.On5V0:
                        status = AardvarkWrapper.aa_target_power(AardvarkHandle, 0x03);
                        if (status == 0x03) return;
                        break;
                    default:
                        throw new ArgumentException(
                            $"{Name}: Case not found for {nameof(target)}={target}");
                }

                var errorMsg = AardvarkWrapper.aa_status_string(status);
                throw new InvalidOperationException($"{Name}: Error {status}, {errorMsg}");
            }
        }

        public void SetPullUpState(EPullupResistors state)
        {
            lock (_instLock)
            {
                CheckIfInitialized();
                Log.Debug($"Setting I2C pull-up resistors to {state}");
                int status;
                switch (state)
                {
                    case EPullupResistors.Off:
                        status = AardvarkWrapper.aa_i2c_pullup(AardvarkHandle, 0x00);
                        if (status == 0x00) return;
                        break;
                    case EPullupResistors.On:
                        status = AardvarkWrapper.aa_i2c_pullup(AardvarkHandle, 0x03);
                        if (status == 0x03) return;
                        break;
                    default:
                        throw new ArgumentException(
                            $"{Name}: Case not found for {nameof(state)}={state}");
                }

                var errorMsg = AardvarkWrapper.aa_status_string(status);
                throw new InvalidOperationException($"{Name}: Error {status}, {errorMsg}");
            }
        }
    }
}