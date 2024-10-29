﻿// Information on the Zebra MS4717 barcode scanner:
// https://www.zebra.com/us/en/products/oem/fixed-mount/ms4700-series.html
// https://www.zebra.com/content/dam/zebra_new_ia/en-us/manuals/oem/ms4717-ig-en.pdf
//
// This instrument driver implements parts of the Zebra's Simple Serial Interface (SSI),
// which enables barcode scanners to communicate with a host over a serial port (UART).
// https://www.google.com/search?q=zebra+simple+serial+interface+programmer+guide
//
// Remember to configure the scanner as a USB Serial Device, not as a USB Keyboard
// https://github.com/diegotacconi/TapExtensions/tree/main/Instruments/TapExtensions.Instruments.BarcodeScanner/ConfigDocs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenTap;
using TapExtensions.Interfaces.BarcodeScanner;
using TapExtensions.Shared.Win32;

namespace TapExtensions.Instruments.BarcodeScanner
{
    [Display("Zebra MS4717",
        Groups: new[] { "TapExtensions", "Instruments", "BarcodeScanner" },
        Description: "Zebra MS4717 Fixed Mount Imager")]
    public class ZebraMS4717 : Instrument, IBarcodeScanner
    {
        #region Settings

        [Display("Connection Address", Order: 1,
            Description: "Examples:\n" +
                         " COM3\n" +
                         " USB\\VID_05E0&PID_1701\n" +
                         " USB\\VID_05E0&PID_1701\\USB_CDC_SYMBOL_SCANNER")]
        public string ConnectionAddress { get; set; }

        [Display("Verbose Logging", Order: 20,
            Description: "Enables verbose logging of serial port (UART) communication.")]
        public bool VerboseLoggingEnabled { get; set; } = true;

        #endregion

        private string _portName;
        private SerialPort _sp;

        public ZebraMS4717()
        {
            // Default values
            Name = nameof(ZebraMS4717);
            ConnectionAddress = @"USB\VID_05E0&PID_1701";

            // Validation rules
            Rules.Add(ValidateConnectionAddress, "Not valid", nameof(ConnectionAddress));
        }

        public override void Open()
        {
            base.Open();
            IsConnected = false;

            _portName = GetSerialPortName();
            CheckIfBarcodeScannerIsAvailable();
        }

        public override void Close()
        {
            CloseSerialPort();
            base.Close();
            IsConnected = false;
        }

        private enum EAddressType
        {
            ComPort,
            UsbDevice
        }

        private bool ValidateConnectionAddress()
        {
            try
            {
                GetAddressType();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private EAddressType GetAddressType()
        {
            // const string comPortPattern = "^COM[1-9][0-9]*$";
            const string comPortPattern = "^[Cc][Oo][Mm][1-9][0-9]*$";
            if (Regex.IsMatch(ConnectionAddress, comPortPattern))
                return EAddressType.ComPort;

            // const string usbDevicePattern = "^USB\\VID_[0-9A-Z]{4}&PID_[0-9A-Z]{4}[0-9A-Z&_\\]+";
            const string usbDevicePattern = "^USB.*";
            if (Regex.IsMatch(ConnectionAddress, usbDevicePattern))
                return EAddressType.UsbDevice;

            throw new InvalidOperationException(
                "Connection Address is not valid");
        }

        private string GetSerialPortName()
        {
            if (GetAddressType() == EAddressType.ComPort)
                return ConnectionAddress;

            if (GetAddressType() == EAddressType.UsbDevice)
                return FindUsbSerialDevice();

            throw new InvalidOperationException(
                "Connection Address is not valid");
        }

        private string FindUsbSerialDevice()
        {
            var usbDeviceAddresses = new List<string> { ConnectionAddress };

            if (usbDeviceAddresses.Count == 0)
                throw new InvalidOperationException(
                    "List of USB Device Address cannot be empty");

            if (VerboseLoggingEnabled)
                Log.Debug("Searching for USB Address(es) of " +
                          $"'{string.Join("', '", usbDeviceAddresses)}'");

            var found = UsbSerialDevices.FindUsbAddress(usbDeviceAddresses);

            Log.Debug($"Found serial port '{found.ComPort}' " +
                      $"with USB Address of '{found.UsbAddress}' " +
                      $"and Description of '{found.Description}'");

            return found.ComPort;
        }

        private void OpenSerialPort()
        {
            if (string.IsNullOrWhiteSpace(_portName))
                throw new InvalidOperationException(
                    "Serial Port Name cannot be empty");

            _sp = new SerialPort
            {
                PortName = _portName,
                BaudRate = 9600,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.RequestToSend,
                ReadTimeout = 1000, // 1 second
                WriteTimeout = 1000 // 1 second
            };

            // Close serial port if already opened
            CloseSerialPort();

            if (VerboseLoggingEnabled)
                Log.Debug($"Opening serial port ({_sp.PortName})");

            // Open serial port
            _sp.Open();
            _sp.DiscardInBuffer();
            _sp.DiscardOutBuffer();
            IsConnected = true;
        }

        private void CloseSerialPort()
        {
            if (!_sp.IsOpen)
                return;

            if (VerboseLoggingEnabled)
                Log.Debug($"Closing serial port ({_sp.PortName})");

            // Close serial port
            _sp.DiscardInBuffer();
            _sp.DiscardOutBuffer();
            _sp.Close();
            _sp.Dispose();
            IsConnected = false;
        }

        private void CheckIfBarcodeScannerIsAvailable()
        {
            OpenSerialPort();
            try
            {
                Wakeup();
                ParamDefaults();
            }
            finally
            {
                CloseSerialPort();
            }
        }

        public byte[] GetRawBytes()
        {
            const int timeout = 5;
            byte[] rawBarcodeLabel;

            OpenSerialPort();
            try
            {
                Wakeup();
                ScanEnable();
                StartSession();
                try
                {
                    // Attempt to read the barcode label
                    rawBarcodeLabel = Read(new byte[0], timeout);
                }
                finally
                {
                    StopSession();
                    ScanDisable();
                    Sleep();
                }
            }
            finally
            {
                CloseSerialPort();
            }

            return rawBarcodeLabel;
        }

        public (string serialNumber, string productCode) GetSerialNumberAndProductCode()
        {
            // Scan barcode label
            var rawBytes = GetRawBytes();

            // Parse barcode label
            var productCode = BarcodeLabelUtility.GetProductCode(rawBytes);
            var serialNumber = BarcodeLabelUtility.GetSerialNumber(rawBytes);

            return (serialNumber, productCode);
        }

        private void WriteRead(byte[] command, byte[] expectedEndOfMessage, int timeout)
        {
            Write(command);
            Read(expectedEndOfMessage, timeout);
        }

        private void Write(byte[] command)
        {
            OnActivity();
            LogBytes(_sp.PortName, ">>", command);
            _sp.DiscardInBuffer();
            _sp.DiscardOutBuffer();
            _sp.Write(command, 0, command.Length);
        }

        private byte[] Read(byte[] expectedResponse, int timeout)
        {
            OnActivity();
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

            if (VerboseLoggingEnabled)
                Log.Debug($"{serialPortName} {direction} {msg}");
        }

        private void Query(byte opCode, byte expectedByte)
        {
            Query(opCode, new byte[0], expectedByte);
        }

        private void Query(byte opCode, byte[] parameters, byte expectedByte, int timeout = 1)
        {
            // Packet Format: <Length> <Opcode> <Message Source> <Status> <Data....> <Checksum>

            var message = new List<byte>
            {
                opCode, // Opcode (1 Byte)
                0x04, // Message Source (1 Byte), 0x00 = Decoder, 0x04 = Host
                0x00 // Status (1 Byte), First time packet, Temporary change
            };

            // Add parameters, if any
            foreach (var parameter in parameters)
                message.Add(parameter);

            // Prepend length (1 Byte)
            // Length of message not including the check sum bytes. Maximum value is 0xFF.
            message.Insert(0, BitConverter.GetBytes(message.Count + 1)[0]);

            // Add checksum (2 Bytes)
            var checksum = CalculateChecksum(message.ToArray());
            message.Add(checksum[1]); // High byte
            message.Add(checksum[0]); // Low byte

            // Send message
            WriteRead(message.ToArray(), new[] { expectedByte }, timeout);
        }

        private static byte[] CalculateChecksum(byte[] bytes)
        {
            // Twos complement of the sum of the message
            int sum = 0;
            foreach (var item in bytes)
                sum += (int)item;

            var sum16 = (ushort)(sum % 255);
            var twosComplement = (ushort)(~sum16 + 1);
            var checksumBytes = BitConverter.GetBytes(twosComplement);
            return checksumBytes;
        }

        #region Zebra's SSI Commands

        private const byte CmdAck = 0xD0;
        private const byte CmdNak = 0xD1;

        private void AimOff()
        {
            // Deactivate aim pattern
            Query(0xC4, CmdAck);
        }

        private void AimOn()
        {
            // Activate aim pattern
            Query(0xC5, CmdAck);
        }

        private void Beep(byte beepCode)
        {
            // Sound the beeper
            Query(0xE6, new[] { beepCode }, CmdAck);
        }

        private void LedOff()
        {
            // Turn off the specified decoder LEDs
            Query(0xE8, new byte[] { 0x00 }, CmdAck);
        }

        private void LedOn()
        {
            // Turn on the specified decoder LEDs
            Query(0xE7, new byte[] { 0x00 }, CmdAck);
        }

        private void ParamDefaults()
        {
            // Set all parameters to their default values
            Query(0xC8, CmdAck);
        }

        private void ParamRequest(byte parameterNumber = 0xFE)
        {
            // Request values of certain parameters
            Query(0xC7, new[] { parameterNumber }, CmdAck);
        }

        private void ScanDisable()
        {
            // Prevents the operator from scanning bar codes
            Query(0xEA, CmdAck);
        }

        private void ScanEnable()
        {
            // Permits bar code scanning
            Query(0xE9, CmdAck);
        }

        private void Sleep()
        {
            // Requests to place the decoder into low power
            Query(0xEB, CmdAck);
        }

        private void StartSession()
        {
            // Tells the decoder to start a scan session
            Query(0xE4, CmdAck);
        }

        private void StopSession()
        {
            // Tells the decoder to abort a decode attempt or video transmission
            Query(0xE5, CmdAck);
        }

        private void Wakeup()
        {
            Write(new byte[] { 0x00 });
            TapThread.Sleep(100);
        }

        #endregion
    }
}