﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MyGameServer.Packets.Control {
	[ControlMessage(ControlPacketType.TimeSyncResponse)]
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public readonly struct TimeSyncResponse {
		public readonly ulong ClientTime;
		public readonly ulong ServerTime;

		public TimeSyncResponse( ulong clientTime, ulong serverTime ) {
			ClientTime = clientTime;
			ServerTime = serverTime;
		}
	}
}
