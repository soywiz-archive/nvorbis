using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Ogg
{
	public class Page
	{
		private static uint[] crc_lookup = new uint[256];

		static Page()
		{
			for (uint i = 0; i < crc_lookup.Length; i++)
			{
				crc_lookup[i] = crc_entry(i);
			}
		}

		private static uint crc_entry(uint index)
		{
			uint r = index << 24;
			for (int i = 0; i < 8; i++)
			{
				if ((r & 0x80000000) != 0)
				{
					r = (r << 1) ^ 0x04c11db7; /* The same as the ethernet generator
								  polynomial, although we use an
							  unreflected alg and an init/final
							  of 0, not 0xffffffff */
				}
				else
				{
					r <<= 1;
				}
			}
			return r;
		}

		public byte[] header_base;
		public int header;
		public int header_len;
		public byte[] body_base;
		public int body;
		public int body_len;

		internal int Version
		{
			get
			{
				return header_base[header + 4] & 0xff;
			}
		}

		internal bool Continued
		{
			get
			{
				return (header_base[header + 5] & 0x01) != 0;
			}
		}

		public bool BegginingOfStream
		{
			get
			{
				return (header_base[header + 5] & 0x02) != 0;
			}
		}

		public bool EndOfStream
		{
			get
			{
				return (header_base[header + 5] & 0x04) != 0;
			}
		}

		public long GranulePosition
		{
			get
			{
				return _ReadGranulePosition(header_base, header + 6);
			}
		}

		static public long _ReadGranulePosition(byte[] header_base, int pos)
		{
			if (BitConverter.IsLittleEndian)
			{
				return BitConverter.ToInt64(header_base, pos);
			}

			ulong foo = 0;
			foo <<= 8; foo |= header_base[pos + 7];
			foo <<= 8; foo |= header_base[pos + 6];
			foo <<= 8; foo |= header_base[pos + 5];
			foo <<= 8; foo |= header_base[pos + 4];
			foo <<= 8; foo |= header_base[pos + 3];
			foo <<= 8; foo |= header_base[pos + 2];
			foo <<= 8; foo |= header_base[pos + 1];
			foo <<= 8; foo |= header_base[pos + 0];
			return (long)foo;
		}

		public int BitStreamSerialNumber
		{
			get
			{
				return (
					(header_base[header + 14] & 0xff) | ((header_base[header + 15] & 0xff) << 8)
					| ((header_base[header + 16] & 0xff) << 16)
					| ((header_base[header + 17] & 0xff) << 24)
				);
			}
		}

		public int PageSequenceNumber
		{
			get
			{
				return (
					(header_base[header + 18] & 0xff) | ((header_base[header + 19] & 0xff) << 8)
					| ((header_base[header + 20] & 0xff) << 16)
					| ((header_base[header + 21] & 0xff) << 24)
				);
			}
		}

		public void WriteChecksum()
		{
			uint crc_reg = 0;

			for (int i = 0; i < header_len; i++)
			{
				crc_reg = (crc_reg << 8) ^ crc_lookup[((crc_reg >> 24) & 0xff) ^ (header_base[header + i] & 0xff)];
			}
			for (int i = 0; i < body_len; i++)
			{
				crc_reg = (crc_reg << 8) ^ crc_lookup[((crc_reg >> 24) & 0xff) ^ (body_base[body + i] & 0xff)];
			}
			header_base[header + 22] = (byte)(crc_reg >> 0);
			header_base[header + 23] = (byte)(crc_reg >> 8);
			header_base[header + 24] = (byte)(crc_reg >> 16);
			header_base[header + 25] = (byte)(crc_reg >> 24);
		}

		public Page Copy()
		{
			return Copy(new Page());
		}

		public Page Copy(Page p)
		{
			byte[] tmp = new byte[header_len];
			Array.Copy(header_base, header, tmp, 0, header_len);
			p.header_len = header_len;
			p.header_base = tmp;
			p.header = 0;
			tmp = new byte[body_len];
			Array.Copy(body_base, body, tmp, 0, body_len);
			p.body_len = body_len;
			p.body_base = tmp;
			p.body = 0;
			return p;
		}

	}

}
