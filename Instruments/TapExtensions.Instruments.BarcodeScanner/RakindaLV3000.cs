﻿// Information on the Rakinda LV3000U and LV3000H barcode scanner:
// https://www.rakinda.com/en/productdetail/83/118/154.html
// https://www.rakinda.com/en/productdetail/83/135/95.html
// https://rakindaiot.com/product/mini-barcode-scanner-lv3000u-2d-with-external-insulation-board/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using OpenTap;
using TapExtensions.Interfaces.BarcodeScanner;

namespace TapExtensions.Instruments.BarcodeScanner
{
    [Display("Rakinda LV3000",
        Groups: new[] { "TapExtensions", "Instruments", "BarcodeScanner" },
        Description: "Rakinda LV3000U or LV3000H Fixed Mount Imager")]
    public class RakindaLV3000 : Instrument, IBarcodeScanner
    {
        #region Settings

        [Display("Serial Port Name", Order: 1)]
        public string SerialPortName { get; set; }

        public enum ELoggingLevel
        {
            Verbose,
            Normal,
            None
        }

        [Display("Logging Level", Order: 2,
            Description: "Level of verbose logging for serial port (UART) communication.")]
        public ELoggingLevel LoggingLevel { get; set; }

        #endregion

        private SerialPort _sp;

        public RakindaLV3000()
        {
            // Default values
            Name = nameof(RakindaLV3000);
            SerialPortName = "COM5";
            LoggingLevel = ELoggingLevel.Normal;
        }

        public override void Open()
        {
            base.Open();

            OpenSerialPort();

            // Send "?" and expect the response to be "!"
            WriteRead(new byte[] { 0x3F }, new byte[] { 0x21 }, 5);

            CloseSerialPort();
        }

        private void OpenSerialPort()
        {
            // Example: USB\VID_1EAB&PID_1D06

            _sp = new SerialPort
            {
                PortName = SerialPortName,
                BaudRate = 9600,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
            };

            // Close serial port if already opened
            CloseSerialPort();

            if (LoggingLevel == ELoggingLevel.Verbose || LoggingLevel == ELoggingLevel.Normal)
                Log.Debug($"Opening serial port ({_sp.PortName})");

            // Open serial port
            _sp.Open();
            _sp.DiscardInBuffer();
            _sp.DiscardOutBuffer();
        }

        public override void Close()
        {
            CloseSerialPort();
            base.Close();
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_sp.IsOpen)
                {
                    if (LoggingLevel == ELoggingLevel.Verbose || LoggingLevel == ELoggingLevel.Normal)
                        Log.Debug($"Closing serial port ({_sp.PortName})");

                    // Close serial port
                    _sp.DiscardInBuffer();
                    _sp.DiscardOutBuffer();
                    _sp.Close();
                    _sp.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex.Message);
            }
        }

        public byte[] GetRawBytes()
        {
            const int timeout = 5;

            OpenSerialPort();

            // Start Scanning
            WriteRead(new byte[] { 0x1B, 0x31 }, new byte[] { 0x06 }, timeout);

            // Attempt to read the barcode label
            var expectedEndOfBarcodeLabel = new byte[] { 0x0D, 0x0A };
            var rawBarcodeLabel = Read(expectedEndOfBarcodeLabel, timeout);

            // Stop Scanning
            WriteRead(new byte[] { 0x1B, 0x30 }, new byte[] { 0x06 }, timeout);

            CloseSerialPort();

            return rawBarcodeLabel;
        }

        private byte[] WriteRead(byte[] command, byte[] expectedEndOfMessage, int timeout)
        {
            Write(command);
            var response = Read(expectedEndOfMessage, timeout);
            return response;
        }

        private void Write(byte[] command)
        {
            LogBytes(_sp.PortName, ">>", command);
            _sp.DiscardInBuffer();
            _sp.DiscardOutBuffer();
            _sp.Write(command, 0, command.Length);
        }

        private byte[] Read(byte[] expectedResponse, int timeout)
        {
            bool responseReceived;
            var response = new List<byte>();
            var timer = new Stopwatch();
            timer.Start();

            do
            {
                TapThread.Sleep(10);
                var count = _sp.BytesToRead;
                var buffer = new byte[count];
                _sp.Read(buffer, 0, count);
                response.AddRange(buffer.ToList());
                responseReceived = FindPattern(response.ToArray(), expectedResponse) >= 0;

                if (timer.Elapsed > TimeSpan.FromSeconds(timeout))
                {
                    Log.Warning("Serial port timed-out!");
                    break;
                }
            } while (!responseReceived);

            timer.Stop();
            LogBytes(_sp.PortName, "<<", response.ToArray());

            if (!responseReceived)
                throw new InvalidOperationException("Did not receive the expected end of message");

            return response.ToArray();
        }

        private static int FindPattern(byte[] source, byte[] pattern)
        {
            var j = -1;
            for (var i = 0; i < source.Length; i++)
                if (source.Skip(i).Take(pattern.Length).SequenceEqual(pattern))
                    j = i;

            return j;
        }

        private void LogBytes(string serialPortName, string direction, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return;

            var msg = new StringBuilder();
            var hex = new StringBuilder();
            var ascii = new StringBuilder();

            foreach (var c in bytes)
            {
                hex.Append(c.ToString("X2") + " ");

                var j = c;
                if (j >= 0x20 && j <= 0x7E)
                {
                    msg.Append((char)j);
                    ascii.Append((char)j + "  ");
                }
                else
                {
                    msg.Append("{" + c.ToString("X2") + "}");
                    ascii.Append('.' + "  ");
                }
            }

            switch (LoggingLevel)
            {
                case ELoggingLevel.Normal:
                    Log.Debug($"{serialPortName} {direction} {msg}");
                    break;

                case ELoggingLevel.Verbose:
                    Log.Debug($"{serialPortName} {direction} Hex:   {hex}");
                    Log.Debug($"{serialPortName} {direction} Ascii: {ascii}");
                    break;
            }
        }
    }
}