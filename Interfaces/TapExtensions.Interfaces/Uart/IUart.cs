﻿using OpenTap;

namespace TapExtensions.Interfaces.Uart
{
    public interface IUart : IDut
    {
        bool Expect(string expectedResponse, int timeout);

        string Query(string command, string expectedEndOfMessage, int timeout);

        void Write(string command);
    }
}