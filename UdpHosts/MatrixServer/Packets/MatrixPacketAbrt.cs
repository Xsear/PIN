﻿using Shared.Udp;
using System.Runtime.InteropServices;
using System.Text;

namespace MatrixServer.Packets
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal unsafe struct MatrixPacketAbrt
    {
        public readonly uint SocketID;
        private fixed byte type[4];

        public string Type
        {
            get
            {
                fixed (byte* t = type)
                {
                    return Utils.ReadFixedString(t, 4);
                }
            }
            set
            {
                fixed (byte* t = type)
                {
                    Utils.WriteFixed(t, Encoding.ASCII.GetBytes(value.Substring(0, 4)));
                }
            }
        }
    }
}