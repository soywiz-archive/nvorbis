using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Ogg
{
	public class StreamState
	{
		/// <summary>
		/// Bytes from packet bodies
		/// </summary>
		byte[] BodyData;

		/// <summary>
		/// Storage elements allocated
		/// </summary>
		int BodyStorage;
		
		/// <summary>
		/// Elements stored; fill mark
		/// </summary>
		int BodyFill;
		
		/// <summary>
		/// Elements of fill returned
		/// </summary>
		private int body_returned;

		/// <summary>
		/// The values that will go to the segment table
		/// </summary>
		int[] LacingVals;
		
		/// <summary>
		/// pcm_pos values for headers. Not compact
		/// this way, but it is simple coupled to the
		/// lacing fifo
		/// </summary>
		long[] GranuleVals;
		int lacing_storage;
		int lacing_fill;
		int lacing_packet;
		int lacing_returned;

		/// <summary>
		/// Working space for header encode
		/// </summary>
		byte[] header = new byte[282];
		int header_fill;

		/// <summary>
		/// Set when we have buffered the last packet
		/// in the logical bitstream.
		/// </summary>
		public int e_o_s;

		/// <summary>
		/// Set after we've written the initial page
		/// of a logical bitstream
		/// </summary>
		int b_o_s;

		/// <summary>
		/// 
		/// </summary>
		int serialno;

		/// <summary>
		/// 
		/// </summary>
		int pageno;

		/// <summary>
		/// Sequence number for decode; the framing
		/// knows where there's a hole in the data,
		/// but we need coupling so that the codec
		/// (which is in a seperate abstraction
		/// layer) also knows about the gap
		/// </summary>
		long packetno;

		/// <summary>
		/// 
		/// </summary>
		long granulepos;

		public StreamState()
		{
			Init();
		}

		StreamState(int SerialNumber)
		{
			//this();
			Init(SerialNumber);
		}

		void Init()
		{
			BodyStorage = 16 * 1024;
			BodyData = new byte[BodyStorage];
			lacing_storage = 1024;
			LacingVals = new int[lacing_storage];
			GranuleVals = new long[lacing_storage];
		}

		public void Init(int SerialNumber)
		{
			if (BodyData == null)
			{
				Init();
			}
			else
			{
				for (int i = 0; i < BodyData.Length; i++) BodyData[i] = 0;
				for (int i = 0; i < LacingVals.Length; i++) LacingVals[i] = 0;
				for (int i = 0; i < GranuleVals.Length; i++) GranuleVals[i] = 0;
			}
			this.serialno = SerialNumber;
		}

		public void Clear()
		{
			BodyData = null;
			LacingVals = null;
			GranuleVals = null;
		}

		void Destroy()
		{
			Clear();
		}

		void BodyExpand(int Needed)
		{
			if (BodyStorage <= BodyFill + Needed)
			{
				BodyStorage += (Needed + 1024);
				byte[] foo = new byte[BodyStorage];
				Array.Copy(BodyData, 0, foo, 0, BodyData.Length);
				BodyData = foo;
			}
		}

		void LacingExpand(int needed)
		{
			if (lacing_storage <= lacing_fill + needed)
			{
				lacing_storage += (needed + 32);
				int[] foo = new int[lacing_storage];
				Array.Copy(LacingVals, 0, foo, 0, LacingVals.Length);
				LacingVals = foo;

				long[] bar = new long[lacing_storage];
				Array.Copy(GranuleVals, 0, bar, 0, GranuleVals.Length);
				GranuleVals = bar;
			}
		}

		/// <summary>
		/// Submit data to the internal buffer of the framing engine.
		/// </summary>
		/// <param name="Packet"></param>
		/// <returns></returns>
		public void PacketIn(Packet Packet)
		{
			int LacingVal = Packet.bytes / 255 + 1;

			if (body_returned != 0)
			{
				// Advance packet data according to the body_returned pointer.
				// We had to keep it around to return a pointer into the buffer last call.

				BodyFill -= body_returned;
				if (BodyFill != 0)
				{
					Array.Copy(BodyData, body_returned, BodyData, 0, BodyFill);
				}
				body_returned = 0;
			}

			// Make sure we have the buffer storage
			BodyExpand(Packet.bytes);
			LacingExpand(LacingVal);

			// Copy in the submitted packet.  Yes, the copy is a waste; this is
			// the liability of overly clean abstraction for the time being.  It
			// will actually be fairly easy to eliminate the extra copy in the
			// future

			Array.Copy(Packet.packet_base, Packet.packet, BodyData, BodyFill, Packet.bytes);
			BodyFill += Packet.bytes;

			// Store lacing vals for this packet
			int j;
			for (j = 0; j < LacingVal - 1; j++)
			{
				LacingVals[lacing_fill + j] = 255;
				GranuleVals[lacing_fill + j] = granulepos;
			}
			LacingVals[lacing_fill + j] = (Packet.bytes) % 255;
			granulepos = GranuleVals[lacing_fill + j] = Packet.granulepos;

			// Flag the first segment as the beginning of the packet
			LacingVals[lacing_fill] |= 0x100;

			lacing_fill += LacingVal;

			// For the sake of completeness
			packetno++;

			if (Packet.e_o_s != 0) e_o_s = 1;
		}

		public int PacketOut(Packet Packet)
		{
			// The last part of decode. We have the stream broken into packet
			// segments.  Now we need to group them into packets (or return the
			// out of sync markers).

			int ptr = lacing_returned;

			if (lacing_packet <= ptr)
			{
				return (0);
			}

			if ((LacingVals[ptr] & 0x400) != 0)
			{
				// We lost sync here; let the app know.
				lacing_returned++;

				// We need to tell the codec there's a gap; it might need to
				// handle previous packet dependencies.
				packetno++;
				return (-1);
			}

			// Gather the whole packet. We'll have no holes or a partial packet
			{
				int size = LacingVals[ptr] & 0xff;
				int bytes = 0;

				Packet.packet_base = BodyData;
				Packet.packet = body_returned;
				Packet.e_o_s = LacingVals[ptr] & 0x200; // last packet of the stream?
				Packet.b_o_s = LacingVals[ptr] & 0x100; // first packet of the stream?
				bytes += size;

				while (size == 255)
				{
					int val = LacingVals[++ptr];
					size = val & 0xff;
					if ((val & 0x200) != 0)
						Packet.e_o_s = 0x200;
					bytes += size;
				}

				Packet.packetno = packetno;
				Packet.granulepos = GranuleVals[ptr];
				Packet.bytes = bytes;

				body_returned += bytes;

				lacing_returned = ptr + 1;
			}
			packetno++;
			return (1);
		}

		/// <summary>
		/// Add the incoming page to the stream state; we decompose the page
		/// into packet segments here as well.
		/// </summary>
		/// <param name="Page"></param>
		/// <returns></returns>
		public int pagein(Page Page)
		{
			byte[] header_base = Page.header_base;
			int header = Page.header;
			byte[] body_base = Page.body_base;
			int body = Page.body;
			int bodysize = Page.body_len;
			int segptr = 0;

			int version = Page.version();
			int continued = Page.continued();
			int bos = Page.bos();
			int eos = Page.eos();
			long granulepos = Page.granulepos();
			int _serialno = Page.serialno();
			int _pageno = Page.pageno();
			int segments = header_base[header + 26] & 0xff;

			// Clean up 'returned data'
			{
				int lr = lacing_returned;
				int br = body_returned;

				// Body data
				if (br != 0)
				{
					BodyFill -= br;
					if (BodyFill != 0)
					{
						Array.Copy(BodyData, br, BodyData, 0, BodyFill);
					}
					body_returned = 0;
				}

				if (lr != 0)
				{
					// segment table
					if ((lacing_fill - lr) != 0)
					{
						Array.Copy(LacingVals, lr, LacingVals, 0, lacing_fill - lr);
						Array.Copy(GranuleVals, lr, GranuleVals, 0, lacing_fill - lr);
					}
					lacing_fill -= lr;
					lacing_packet -= lr;
					lacing_returned = 0;
				}
			}

			// check the serial number
			if (_serialno != serialno)
				return (-1);
			if (version > 0)
				return (-1);

			LacingExpand(segments + 1);

			// are we in sequence?
			if (_pageno != pageno)
			{
				int i;

				// unroll previous partial packet (if any)
				for (i = lacing_packet; i < lacing_fill; i++)
				{
					BodyFill -= LacingVals[i] & 0xff;
					//System.out.println("??");
				}
				lacing_fill = lacing_packet;

				// make a note of dropped data in segment table
				if (pageno != -1)
				{
					LacingVals[lacing_fill++] = 0x400;
					lacing_packet++;
				}

				// are we a 'continued packet' page?  If so, we'll need to skip
				// some segments
				if (continued != 0)
				{
					bos = 0;
					for (; segptr < segments; segptr++)
					{
						int val = (header_base[header + 27 + segptr] & 0xff);
						body += val;
						bodysize -= val;
						if (val < 255)
						{
							segptr++;
							break;
						}
					}
				}
			}

			if (bodysize != 0)
			{
				BodyExpand(bodysize);
				Array.Copy(body_base, body, BodyData, BodyFill, bodysize);
				BodyFill += bodysize;
			}

			{
				int saved = -1;
				while (segptr < segments)
				{
					int val = (header_base[header + 27 + segptr] & 0xff);
					LacingVals[lacing_fill] = val;
					GranuleVals[lacing_fill] = -1;

					if (bos != 0)
					{
						LacingVals[lacing_fill] |= 0x100;
						bos = 0;
					}

					if (val < 255)
						saved = lacing_fill;

					lacing_fill++;
					segptr++;

					if (val < 255)
						lacing_packet = lacing_fill;
				}

				// set the granulepos on the last pcmval of the last full packet
				if (saved != -1)
				{
					GranuleVals[saved] = granulepos;
				}
			}

			if (eos != 0)
			{
				e_o_s = 1;
				if (lacing_fill > 0)
					LacingVals[lacing_fill - 1] |= 0x200;
			}

			pageno = _pageno + 1;
			return (0);
		}

		/// <summary>
		/// This will flush remaining packets into a page (returning nonzero),
		/// even if there is not enough data to trigger a flush normally
		/// (undersized page). If there are no packets or partial packets to
		/// flush, ogg_stream_flush returns 0.  Note that ogg_stream_flush will
		/// try to flush a normal sized page like ogg_stream_pageout; a call to
		/// ogg_stream_flush does not gurantee that all packets have flushed.
		/// Only a return value of 0 from ogg_stream_flush indicates all packet
		/// data is flushed into pages.
		/// ogg_stream_page will flush the last page in a stream even if it's
		/// undersized; you almost certainly want to use ogg_stream_pageout
		/// (and *not* ogg_stream_flush) unless you need to flush an undersized
		/// page in the middle of a stream for some reason.
		/// </summary>
		/// <param name="og"></param>
		/// <returns></returns>
		public int flush(Page og)
		{

			int i;
			int vals = 0;
			int maxvals = (lacing_fill > 255 ? 255 : lacing_fill);
			int bytes = 0;
			int acc = 0;
			ulong granule_pos = (ulong)GranuleVals[0];

			if (maxvals == 0)
				return (0);

			/* construct a page */
			/* decide how many segments to include */

			/* If this is the initial header case, the first page must only include
			   the initial header packet */
			if (b_o_s == 0)
			{ /* 'initial header page' case */
				granule_pos = 0;
				for (vals = 0; vals < maxvals; vals++)
				{
					if ((LacingVals[vals] & 0x0ff) < 255)
					{
						vals++;
						break;
					}
				}
			}
			else
			{
				for (vals = 0; vals < maxvals; vals++)
				{
					if (acc > 4096)
						break;
					acc += (LacingVals[vals] & 0x0ff);
					granule_pos = (ulong)GranuleVals[vals];
				}
			}

			// construct the header in temp storage
			Array.Copy(Encoding.ASCII.GetBytes("OggS"), 0, header, 0, 4);

			// stream structure version
			header[4] = 0x00;

			// continued packet flag?
			header[5] = 0x00;
			if ((LacingVals[0] & 0x100) == 0) header[5] |= 0x01;
			// first page flag?
			if (b_o_s == 0) header[5] |= 0x02;
			// last page flag?
			if (e_o_s != 0 && lacing_fill == vals) header[5] |= 0x04;
			b_o_s = 1;

			// 64 bits of PCM position
			for (i = 6; i < 14; i++)
			{
				header[i] = (byte)granule_pos;
				granule_pos >>= 8;
			}

			// 32 bits of stream serial number
			{
				uint _serialno = (uint)serialno;
				for (i = 14; i < 18; i++)
				{
					header[i] = (byte)_serialno;
					_serialno >>= 8;
				}
			}

			// 32 bits of page counter (we have both counter and page header
			// because this val can roll over)
			if (pageno == -1)
				// because someone called
				// stream_reset; this would be a
				// strange thing to do in an
				// encode stream, but it has
				// plausible uses
				pageno = 0;
			{
				uint _pageno = (uint)pageno++;
				for (i = 18; i < 22; i++)
				{
					header[i] = (byte)_pageno;
					_pageno >>= 8;
				}
			}

			// zero for computation; filled in later
			header[22] = 0;
			header[23] = 0;
			header[24] = 0;
			header[25] = 0;

			// segment table
			header[26] = (byte)vals;
			for (i = 0; i < vals; i++)
			{
				header[i + 27] = (byte)LacingVals[i];
				bytes += (header[i + 27] & 0xff);
			}

			// set pointers in the ogg_page struct
			og.header_base = header;
			og.header = 0;
			og.header_len = header_fill = vals + 27;
			og.body_base = BodyData;
			og.body = body_returned;
			og.body_len = bytes;

			// advance the lacing data and set the body_returned pointer

			lacing_fill -= vals;
			Array.Copy(LacingVals, vals, LacingVals, 0, lacing_fill * 4);
			Array.Copy(GranuleVals, vals, GranuleVals, 0, lacing_fill * 8);
			body_returned += bytes;

			// calculate the checksum
			og.checksum();

			// done
			return 1;
		}

		/// <summary>
		/// This constructs pages from buffered packet segments.  The pointers
		/// returned are to static buffers; do not free. The returned buffers are
		/// good only until the next call (using the same ogg_stream_state)
		/// </summary>
		/// <param name="og"></param>
		/// <returns></returns>
		public int pageout(Page og)
		{
			if (
				(e_o_s != 0 && lacing_fill != 0) || // 'were done, now flush' case
				BodyFill - body_returned > 4096 || // 'page nominal size' case
				lacing_fill >= 255 || // segment table full' case
				(lacing_fill != 0 && b_o_s == 0)
			)
			{
				// 'initial header page' case
				return flush(og);
			}
			return 0;
		}

		public int Eof()
		{
			return e_o_s;
		}

		public int Reset()
		{
			BodyFill = 0;
			body_returned = 0;

			lacing_fill = 0;
			lacing_packet = 0;
			lacing_returned = 0;

			header_fill = 0;

			e_o_s = 0;
			b_o_s = 0;
			pageno = -1;
			packetno = 0;
			granulepos = 0;
			return (0);
		}
	}
}
