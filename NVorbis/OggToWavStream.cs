//#define CAN_SEEK

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using NVorbis.Ogg;
using NVorbis.Vorbis;
using NVorbis.Extra;
using System.Diagnostics;

namespace NVorbis
{
    public class OggToWavStream : Stream
    {
		Stream WaveOutputStream;
		Stream BufferStream;
		Stream OggBaseStream;

        public OggToWavStream(Stream OggBaseStream)
        {
            this.OggBaseStream = OggBaseStream;
            this.BufferStream = new MemoryStream();
			DecodeInit();
            DecodeTo(this.BufferStream);
            BufferStream.Position = 0;

            var WaveStream = new WaveStream();
            WaveOutputStream = new MemoryStream();
            BufferStream.Position = 0;
            WaveStream.WriteWave(WaveOutputStream, () =>
            {
#if !UNSAFE
				int BufferSize = 4096;
				var Buffer = new byte[BufferSize];
				while (true)
				{
					int Readed = BufferStream.Read(Buffer, 0, BufferSize);
					if (Readed <= 0) break;
					WaveOutputStream.Write(Buffer, 0, Readed);
				}
#else
                BufferStream.CopyTo(WaveOutputStream);
#endif
            }, NumberOfChannels: 1, SampleRate: 44100);
			WaveOutputStream.Position = 0;
        }

		int convsize = 4096 * 2;
		byte[] convbuffer;

		SyncState SyncState;
		StreamState StreamState;
		Page Page;
		Packet Packet;

		Info Info;
		Comment Comment;
		DspState DspState;
		Block Block;

		byte[] buffer;
		int bytes = 0;

		private void DecodeInit()
		{
			convbuffer = new byte[convsize]; // take 8k out of the data segment, not the stack
			SyncState = new SyncState(); // sync and verify incoming physical bitstream
			StreamState = new StreamState(); // take physical pages, weld into a logical stream of packets
			Page = new Page(); // one Ogg bitstream page.  Vorbis packets are inside
			Packet = new Packet(); // one raw packet of data for decode

			Info = new Info(); // struct that stores all the static vorbis bitstream settings
			Comment = new Comment(); // struct that stores all the bitstream user comments
			DspState = new DspState(); // central working state for the packet->PCM decoder
			Block = new Block(DspState); // local working space for packet->PCM decode
			bytes = 0;
		}

		private void DecodeTo(Stream OutputBuffer)
        {
            // Decode setup

            SyncState.Init(); // Now we can read pages

            // we repeat if the bitstream is chained
            while (true)
            {
                bool eos = false;

                // grab some data at the head of the stream.  We want the first page
                // (which is guaranteed to be small and only contain the Vorbis
                // stream initial header) We need the first page to get the stream
                // serialno.

                // submit a 4k block to libvorbis' Ogg layer
                int index = SyncState.Buffer(4096);
                buffer = SyncState.Data;
                try
                {
                    bytes = OggBaseStream.Read(buffer, index, 4096);
                }
                catch (Exception e)
                {
					Debug.WriteLine(e);
                    return;
                }
                SyncState.wrote(bytes);

                // Get the first page.
                int _result = SyncState.PageOut(Page);
                if (_result != 1)
                {
                    // have we simply run out of data?  If so, we're done.
                    if (bytes < 4096) break;

					Debug.WriteLine(bytes + "; " + _result);
                    //File.WriteAllBytes();
                    // error case.  Must not be Vorbis data
					Debug.WriteLine("Input does not appear to be an Ogg bitstream.");
                    return;
                }

                // Get the serial number and set up the rest of decode.
                // serialno first; use it to set up a logical stream
                StreamState.Init(Page.serialno());

                // extract the initial header from the first page and verify that the
                // Ogg bitstream is in fact Vorbis data

                // I handle the initial header first instead of just having the code
                // read all three Vorbis headers at once because reading the initial
                // header is an easy way to identify a Vorbis bitstream and it's
                // useful to see that functionality seperated out.

                Info.init();
                Comment.init();
				// error; stream version mismatch perhaps
				if (StreamState.pagein(Page) < 0) throw (new Exception("Error reading first page of Ogg bitstream data."));
				// no page? must not be vorbis
				if (StreamState.PacketOut(Packet) != 1) throw (new Exception("Error reading initial header packet."));
				// error case; not a vorbis header
				if (Info.synthesis_headerin(Comment, Packet) < 0) throw (new Exception("This Ogg bitstream does not contain Vorbis audio data."));

                // At this point, we're sure we're Vorbis.  We've set up the logical
                // (Ogg) bitstream decoder.  Get the comment and codebook headers and
                // set up the Vorbis decoder

                // The next two packets in order are the comment and codebook headers.
                // They're likely large and may span multiple pages.  Thus we reead
                // and submit data until we get our two pacakets, watching that no
                // pages are missing.  If a page is missing, error out; losing a
                // header page is the only place where missing data is fatal. */

                int i = 0;
                while (i < 2)
                {
                    while (i < 2)
                    {

                        int result = SyncState.PageOut(Page);
                        if (result == 0) break; // Need more data
                        // Don't complain about missing or corrupt data yet.  We'll
                        // catch it at the packet output phase

                        if (result == 1)
                        {
                            StreamState.pagein(Page); // we can ignore any errors here
                            // as they'll also become apparent
                            // at packetout
                            while (i < 2)
                            {
                                result = StreamState.PacketOut(Packet);
                                if (result == 0) break;

								// Uh oh; data at some point was corrupted or missing!
								// We can't tolerate that in a header.  Die.
								if (result == -1) throw (new Exception("Corrupt secondary header.  Exiting."));
                                Info.synthesis_headerin(Comment, Packet);
                                i++;
                            }
                        }
                    }
                    // no harm in not checking before adding more
                    index = SyncState.Buffer(4096);
                    buffer = SyncState.Data;
					bytes = OggBaseStream.Read(buffer, index, 4096);
                    if (bytes == 0 && i < 2)
                    {
                        throw (new Exception("End of file before finding all Vorbis headers!"));
                    }
                    SyncState.wrote(bytes);
                }

                // Throw the comments plus a few lines about the bitstream we're
                // decoding
                {
                    byte[][] ptr = Comment.user_comments;
                    for (int j = 0; j < ptr.Length; j++)
                    {
                        if (ptr[j] == null) break;
						Debug.WriteLine(Util.InternalEncoding.GetString(ptr[j], 0, ptr[j].Length - 1));
                    }
					Debug.WriteLine("\nBitstream is {0} channel, {1}Hz", Info.channels, Info.rate);
					Debug.WriteLine(
                        "Encoded by: {0}\n",
                        Util.InternalEncoding.GetString(Comment.vendor, 0, Comment.vendor.Length - 1)
					);
                }

                convsize = 4096 / Info.channels;

                // OK, got and parsed all three headers. Initialize the Vorbis
                //  packet->PCM decoder.
                DspState.synthesis_init(Info); // central decode state
                Block.init(DspState); // local state for most of the decode
                // so multiple block decodes can
                // proceed in parallel.  We could init
                // multiple vorbis_block structures
                // for vd here

                float[][][] _pcm = new float[1][][];
                int[] _index = new int[Info.channels];
                // The rest is just a straight decode loop until end of stream
                while (!eos)
                {
                    while (!eos)
                    {

                        int result = SyncState.PageOut(Page);
                        if (result == 0) break; // need more data
                        if (result == -1)
                        {
							// missing or corrupt data at this page position
                            Debug.WriteLine("Corrupt or missing data in bitstream; continuing...");
                        }
                        else
                        {
                            StreamState.pagein(Page); // can safely ignore errors at
                            // this point
                            while (true)
                            {
                                result = StreamState.PacketOut(Packet);

                                if (result == 0)
                                    break; // need more data
                                if (result == -1)
                                { // missing or corrupt data at this page position
                                    // no reason to complain; already complained above
                                }
                                else
                                {
                                    // we have a packet.  Decode it
                                    int samples;
                                    if (Block.synthesis(Packet) == 0)
                                    { // test for success!
                                        DspState.synthesis_blockin(Block);
                                    }

                                    // **pcm is a multichannel float vector.  In stereo, for
                                    // example, pcm[0] is left, and pcm[1] is right.  samples is
                                    // the size of each channel.  Convert the float values
                                    // (-1.<=range<=1.) to whatever PCM format and write it out

                                    while ((samples = DspState.synthesis_pcmout(_pcm, _index)) > 0)
                                    {
                                        float[][] pcm = _pcm[0];
                                        int bout = (samples < convsize ? samples : convsize);

                                        // convert floats to 16 bit signed ints (host order) and
                                        // interleave
                                        for (i = 0; i < Info.channels; i++)
                                        {
                                            int ptr = i * 2;
                                            //int ptr=i;
                                            int mono = _index[i];
                                            for (int j = 0; j < bout; j++)
                                            {
                                                int val = (int)(pcm[i][mono + j] * 32767.0);
                                                //		      short val=(short)(pcm[i][mono+j]*32767.);
                                                //		      int val=(int)Math.round(pcm[i][mono+j]*32767.);
                                                // might as well guard against clipping
												if (val > 32767) val = 32767;
												if (val < -32768) val = -32768;
                                                if (val < 0) val = val | 0x8000;
                                                convbuffer[ptr] = (byte)(val);
                                                convbuffer[ptr + 1] = (byte)(((uint)val) >> 8);
                                                ptr += 2 * (Info.channels);
                                            }
                                        }

                                        //                  System.out.write(convbuffer, 0, 2*vi.channels*bout);
                                        //throw(new NotImplementedException("ccccccccc"));
                                        OutputBuffer.Write(convbuffer, 0, 2 * Info.channels * bout);

                                        // tell libvorbis how
                                        // many samples we
                                        // actually consumed
                                        DspState.synthesis_read(bout);
                                    }
                                }
                            }
                            if (Page.eos() != 0)
                                eos = true;
                        }
                    }

                    if (!eos)
                    {
                        index = SyncState.Buffer(4096);
                        buffer = SyncState.Data;
						bytes = OggBaseStream.Read(buffer, index, 4096);
                        SyncState.wrote(bytes);
                        if (bytes == 0) eos = true;
                    }
                }

                // clean up this logical bitstream; before exit we see if we're
                // followed by another [chained]

                StreamState.Clear();

                // ogg_page and ogg_packet structs always point to storage in
                // libvorbis.  They're never freed or manipulated directly

                Block.clear();
                DspState.clear();
                Info.Clear(); // must be called last
            }

            // OK, clean up the framer
            SyncState.Clear();
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get
			{
#if !CAN_SEEK
				return false;
#else
				return true;
#endif
			}
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
        }

        public override long Length
        {
            get
			{
#if false
				throw(new NotImplementedException());
#else
				return WaveOutputStream.Length;
#endif
			}
        }

        public override long Position
        {
            get
            {
                return WaveOutputStream.Position;
            }
            set
            {
#if !CAN_SEEK
				throw(new NotImplementedException("Can't seek"));
#else
                WaveOutputStream.Position = value;
#endif
			}
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return WaveOutputStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
#if !CAN_SEEK
				throw(new NotImplementedException("Can't seek"));
#else
				return WaveOutputStream.Seek(offset, origin);
#endif
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
