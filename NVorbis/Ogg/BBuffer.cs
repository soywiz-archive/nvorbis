using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Ogg
{
	public sealed class BBuffer
	{
		private const int BUFFER_INCREMENT = 256;

		private readonly uint[] Mask = {
			0x00000000, 0x00000001, 0x00000003, 0x00000007,
			0x0000000f, 0x0000001f, 0x0000003f, 0x0000007f,
			0x000000ff, 0x000001ff, 0x000003ff, 0x000007ff,
			0x00000fff, 0x00001fff, 0x00003fff, 0x00007fff,
			0x0000ffff, 0x0001ffff, 0x0003ffff, 0x0007ffff,
			0x000fffff, 0x001fffff, 0x003fffff, 0x007fffff,
			0x00ffffff, 0x01ffffff, 0x03ffffff, 0x07ffffff,
			0x0fffffff, 0x1fffffff, 0x3fffffff, 0x7fffffff,
			0xffffffff
		};

		private int Offset = 0;
		private byte[] Data = null;
		private int SizeInBitsRemaining = 0;
		private int SizeInBytes = 0;
		private int InternalBufferSize = 0;

		public void WriteInit()
		{
			Data = new byte[BUFFER_INCREMENT];
			Offset = 0;
			Data[0] = 0;
			InternalBufferSize = BUFFER_INCREMENT;
		}

		public void Write(byte[] Data)
		{
			for (int i = 0; i < Data.Length; i++)
			{
				if (Data[i] == 0) break;
				Write(Data[i], 8);
			}
		}

		public void Read(byte[] s, int bytes)
		{
			int i = 0;
			while (bytes-- != 0)
			{
				s[i++] = (byte)(Read(8));
			}
		}

		void Reset()
		{
			Offset = 0;
			Data[0] = 0;
			SizeInBitsRemaining = SizeInBytes = 0;
		}

		public void WriteClear()
		{
			Data = null;
		}

		public void ReadInit(byte[] Buffer, int BytesCount)
		{
			ReadInit(Buffer, 0, BytesCount);
		}

		public void ReadInit(byte[] Buffer, int Start, int BytesCount)
		{
			Offset = Start;
			Data = Buffer;
			SizeInBitsRemaining = SizeInBytes = 0;
			InternalBufferSize = BytesCount;
		}

		public void Write(int _value, int BitCount)
		{
			uint Value = (uint)_value;
			if (SizeInBytes + 4 >= InternalBufferSize)
			{
				byte[] foo = new byte[InternalBufferSize + BUFFER_INCREMENT];
				Array.Copy(Data, 0, foo, 0, InternalBufferSize);
				Data = foo;
				InternalBufferSize += BUFFER_INCREMENT;
			}

			Value &= Mask[BitCount];
			BitCount += SizeInBitsRemaining;
			Data[Offset] |= (byte)(Value << SizeInBitsRemaining);

			if (BitCount >= 8)
			{
				Data[Offset + 1] = (byte)(Value >> (8 - SizeInBitsRemaining));
				if (BitCount >= 16)
				{
					Data[Offset + 2] = (byte)(Value >> (16 - SizeInBitsRemaining));
					if (BitCount >= 24)
					{
						Data[Offset + 3] = (byte)(Value >> (24 - SizeInBitsRemaining));
						if (BitCount >= 32)
						{
							if (SizeInBitsRemaining > 0)
								Data[Offset + 4] = (byte)(Value >> (32 - SizeInBitsRemaining));
							else
								Data[Offset + 4] = 0;
						}
					}
				}
			}

			SizeInBytes += BitCount / 8;
			Offset += BitCount / 8;
			SizeInBitsRemaining = BitCount & 7;
		}

		public int Look(int bits)
		{
			int ret;
			uint m = Mask[bits];

			bits += SizeInBitsRemaining;

			if (SizeInBytes + 4 >= InternalBufferSize)
			{
				if (SizeInBytes + (bits - 1) / 8 >= InternalBufferSize)
					return (-1);
			}

			ret = ((Data[Offset]) & 0xff) >> SizeInBitsRemaining;
			if (bits > 8)
			{
				ret |= ((Data[Offset + 1]) & 0xff) << (8 - SizeInBitsRemaining);
				if (bits > 16)
				{
					ret |= ((Data[Offset + 2]) & 0xff) << (16 - SizeInBitsRemaining);
					if (bits > 24)
					{
						ret |= ((Data[Offset + 3]) & 0xff) << (24 - SizeInBitsRemaining);
						if (bits > 32 && SizeInBitsRemaining != 0)
						{
							ret |= ((Data[Offset + 4]) & 0xff) << (32 - SizeInBitsRemaining);
						}
					}
				}
			}
			return (int)(m & ret);
		}

		public int Look1()
		{
			if (SizeInBytes >= InternalBufferSize) return (-1);
			return ((Data[Offset] >> SizeInBitsRemaining) & 1);
		}

		public void adv(int bits)
		{
			bits += SizeInBitsRemaining;
			Offset += bits / 8;
			SizeInBytes += bits / 8;
			SizeInBitsRemaining = bits & 7;
		}

		public void adv1()
		{
			++SizeInBitsRemaining;
			if (SizeInBitsRemaining > 7)
			{
				SizeInBitsRemaining = 0;
				Offset++;
				SizeInBytes++;
			}
		}

		public int Read(int Bits)
		{
			uint Return;
			uint m = Mask[Bits];

			Bits += SizeInBitsRemaining;

			if (SizeInBytes + 4 >= InternalBufferSize)
			{
				Return = unchecked((uint)-1);
				if (SizeInBytes + (Bits - 1) / 8 >= InternalBufferSize)
				{
					Offset += Bits / 8;
					SizeInBytes += Bits / 8;
					SizeInBitsRemaining = Bits & 7;
					return (int)(Return);
				}
			}

			Return = (uint)((Data[Offset]) & 0xff) >> SizeInBitsRemaining;
			if (Bits > 8)
			{
				Return |= (uint)((Data[Offset + 1]) & 0xff) << (8 - SizeInBitsRemaining);
				if (Bits > 16)
				{
					Return |= (uint)((Data[Offset + 2]) & 0xff) << (16 - SizeInBitsRemaining);
					if (Bits > 24)
					{
						Return |= (uint)((Data[Offset + 3]) & 0xff) << (24 - SizeInBitsRemaining);
						if (Bits > 32 && SizeInBitsRemaining != 0)
						{
							Return |= (uint)((Data[Offset + 4]) & 0xff) << (32 - SizeInBitsRemaining);
						}
					}
				}
			}

			Return &= m;

			Offset += Bits / 8;
			SizeInBytes += Bits / 8;
			SizeInBitsRemaining = Bits & 7;
			return (int)(Return);
		}

		public int ReadB(int bits)
		{
			int ret;
			int m = 32 - bits;

			bits += SizeInBitsRemaining;

			if (SizeInBytes + 4 >= InternalBufferSize)
			{
				/* not the main path */
				ret = -1;
				if (SizeInBytes * 8 + bits > InternalBufferSize * 8)
				{
					Offset += bits / 8;
					SizeInBytes += bits / 8;
					SizeInBitsRemaining = bits & 7;
					return (ret);
				}
			}

			ret = (Data[Offset] & 0xff) << (24 + SizeInBitsRemaining);
			if (bits > 8)
			{
				ret |= (Data[Offset + 1] & 0xff) << (16 + SizeInBitsRemaining);
				if (bits > 16)
				{
					ret |= (Data[Offset + 2] & 0xff) << (8 + SizeInBitsRemaining);
					if (bits > 24)
					{
						ret |= (Data[Offset + 3] & 0xff) << (SizeInBitsRemaining);
						if (bits > 32 && (SizeInBitsRemaining != 0))
						{
							ret |= (Data[Offset + 4] & 0xff) >> (8 - SizeInBitsRemaining);
						}
					}
				}
			}
			// CHECK!
			//ret=(ret>>>(m>>1))>>>((m+1)>>1);

			ret = (int)(uint)((uint)ret >> (m >> 1)) >> ((m + 1) >> 1);

			Offset += bits / 8;
			SizeInBytes += bits / 8;
			SizeInBitsRemaining = bits & 7;
			return (ret);
		}

		public int Read1()
		{
			int ret;
			if (SizeInBytes >= InternalBufferSize)
			{
				ret = -1;
				SizeInBitsRemaining++;
				if (SizeInBitsRemaining > 7)
				{
					SizeInBitsRemaining = 0;
					Offset++;
					SizeInBytes++;
				}
				return (ret);
			}

			ret = (Data[Offset] >> SizeInBitsRemaining) & 1;

			SizeInBitsRemaining++;
			if (SizeInBitsRemaining > 7)
			{
				SizeInBitsRemaining = 0;
				Offset++;
				SizeInBytes++;
			}
			return (ret);
		}

		public int bytes()
		{
			return (SizeInBytes + (SizeInBitsRemaining + 7) / 8);
		}

		public int bits()
		{
			return (SizeInBytes * 8 + SizeInBitsRemaining);
		}

		public byte[] buffer()
		{
			return (Data);
		}

		public static int ilog(uint v)
		{
			int ret = 0;
			while (v > 0)
			{
				ret++;
				v >>= 1;
			}
			return (ret);
		}
	}

}
