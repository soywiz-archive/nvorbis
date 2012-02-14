using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Ogg
{
	/// <summary>
	/// DECODING PRIMITIVES: packet streaming layer
	/// 
	/// This has two layers to place more of the multi-serialno and paging
	/// control in the application's hands.  First, we expose a data buffer
	/// using ogg_decode_buffer().  The app either copies into the
	/// buffer, or passes it directly to read(), etc.  We then call
	/// ogg_decode_wrote() to tell how many bytes we just added.
	/// 
	/// Pages are returned (pointers into the buffer in ogg_sync_state)
	/// by ogg_decode_stream().  The page is then submitted to
	/// ogg_decode_page() along with the appropriate
	/// ogg_stream_state* (ie, matching serialno).  We then get raw
	/// packets out calling ogg_stream_packet() with a
	/// ogg_stream_state.  See the 'frame-prog.txt' docs for details and
	/// example code.
	/// </summary>
	public class SyncState
	{

		public byte[] Data;
		int Storage;
		int Fill;
		int Returned;

		int Unsynced;
		int HeaderBytes;
		int BodyBytes;

		public void Clear()
		{
			Data = null;
		}

		public int Buffer(int Size)
		{
			// first, clear out any space that has been previously returned
			if (Returned != 0)
			{
				Fill -= Returned;
				if (Fill > 0)
				{
					Array.Copy(Data, Returned, Data, 0, Fill);
				}
				Returned = 0;
			}

			if (Size > Storage - Fill)
			{
				// We need to extend the internal buffer
				int newsize = Size + Fill + 4096; // an extra page to be nice
				if (Data != null)
				{
					byte[] foo = new byte[newsize];
					Array.Copy(Data, 0, foo, 0, Data.Length);
					Data = foo;
				}
				else
				{
					Data = new byte[newsize];
				}
				Storage = newsize;
			}

			return (Fill);
		}

		public int Wrote(int bytes)
		{
			if (Fill + bytes > Storage) return (-1);
			Fill += bytes;
			return (0);
		}

		// sync the stream.  This is meant to be useful for finding page
		// boundaries.
		//
		// return values for this:
		// -n) skipped n bytes
		//  0) page not ready; more data (no bytes skipped)
		//  n) page synced at current location; page length n bytes
		private Page _pageseek = new Page();
		private byte[] chksum = new byte[4];

		public int PageSeek(Page og)
		{
			int page = Returned;
			int next;
			int bytes = Fill - Returned;

			if (HeaderBytes == 0)
			{
				int _headerbytes, i;
				if (bytes < 27)
					return (0); // not enough for a header

				/* verify capture pattern */
				if (Data[page] != 'O' || Data[page + 1] != 'g' || Data[page + 2] != 'g'
					|| Data[page + 3] != 'S')
				{
					HeaderBytes = 0;
					BodyBytes = 0;

					// search for possible capture
					next = 0;
					for (int ii = 0; ii < bytes - 1; ii++)
					{
						if (Data[page + 1 + ii] == 'O')
						{
							next = page + 1 + ii;
							break;
						}
					}
					//next=memchr(page+1,'O',bytes-1);
					if (next == 0)
						next = Fill;

					Returned = next;
					return (-(next - page));
				}
				_headerbytes = (Data[page + 26] & 0xff) + 27;
				if (bytes < _headerbytes)
					return (0); // not enough for header + seg table

				// count up body length in the segment table

				for (i = 0; i < (Data[page + 26] & 0xff); i++)
				{
					BodyBytes += (Data[page + 27 + i] & 0xff);
				}
				HeaderBytes = _headerbytes;
			}

			if (BodyBytes + HeaderBytes > bytes)
				return (0);

			// The whole test page is buffered.  Verify the checksum
			lock (chksum)
			{
				// Grab the checksum bytes, set the header field to zero

				Array.Copy(Data, page + 22, chksum, 0, 4);
				Data[page + 22] = 0;
				Data[page + 23] = 0;
				Data[page + 24] = 0;
				Data[page + 25] = 0;

				// set up a temp page struct and recompute the checksum
				Page log = _pageseek;
				log.header_base = Data;
				log.header = page;
				log.header_len = HeaderBytes;

				log.body_base = Data;
				log.body = page + HeaderBytes;
				log.body_len = BodyBytes;
				log.WriteChecksum();

				// Compare
				if (chksum[0] != Data[page + 22] || chksum[1] != Data[page + 23]
					|| chksum[2] != Data[page + 24] || chksum[3] != Data[page + 25])
				{
					// D'oh.  Mismatch! Corrupt page (or miscapture and not a page at all)
					// replace the computed checksum with the one actually read in
					Array.Copy(chksum, 0, Data, page + 22, 4);
					// Bad checksum. Lose sync */

					HeaderBytes = 0;
					BodyBytes = 0;
					// search for possible capture
					next = 0;
					for (int ii = 0; ii < bytes - 1; ii++)
					{
						if (Data[page + 1 + ii] == 'O')
						{
							next = page + 1 + ii;
							break;
						}
					}
					//next=memchr(page+1,'O',bytes-1);
					if (next == 0)
						next = Fill;
					Returned = next;
					return (-(next - page));
				}
			}

			// yes, have a whole page all ready to go
			{
				page = Returned;

				if (og != null)
				{
					og.header_base = Data;
					og.header = page;
					og.header_len = HeaderBytes;
					og.body_base = Data;
					og.body = page + HeaderBytes;
					og.body_len = BodyBytes;
				}

				Unsynced = 0;
				Returned += (bytes = HeaderBytes + BodyBytes);
				HeaderBytes = 0;
				BodyBytes = 0;
				return (bytes);
			}
		}

		/// <summary>
		/// sync the stream and get a page.  Keep trying until we find a page.
		/// Supress 'sync errors' after reporting the first.
		/// 
		/// </summary>
		/// <param name="og"></param>
		/// <returns>
		/// -1) recapture (hole in data)
		///  0) need more data
		///  1) page returned
		///  
		///  Returns pointers into buffered data; invalidated by next call to
		///  _stream, _clear, _init, or _buffer
		/// </returns>
		public int PageOut(Page og)
		{
			// All we need to do is verify a page at the head of the
			// stream buffer.  If it doesn't verify, we look for the
			// next potential frame.

			while (true)
			{
				int ret = PageSeek(og);
	
				// have a page
				if (ret > 0) return 1;

				// need more data
				if (ret == 0) return 0;

				// head did not start a synced page... skipped some bytes
				if (Unsynced == 0)
				{
					Unsynced = 1;
					return -1;
				}

				// loop. keep looking
			}
		}

		/// <summary>
		/// Clear things to an initial state.
		/// Good to call, eg, before seeking.
		/// </summary>
		/// <returns></returns>
		public int Reset()
		{
			Fill = 0;
			Returned = 0;
			Unsynced = 0;
			HeaderBytes = 0;
			BodyBytes = 0;
			return (0);
		}

		public void Init()
		{
		}

		public int DataOffset
		{
			get
			{
				return Returned;
			}
		}

		public int BufferOffset
		{
			get
			{
				return Fill;
			}
		}
	}
}
