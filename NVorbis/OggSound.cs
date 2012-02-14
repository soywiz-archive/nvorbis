#define MULTITHREADED

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Audio;
using NVorbis.Ogg;
using NVorbis.Vorbis;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace CurseXna.NVorbis
{
	public class OggSound : IDisposable
	{
		DynamicSoundEffectInstance DynamicSoundEffect;

		/*
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
		*/

		public class InfoHolderClass
		{
			public Info Info;
			public int BufferLength;
			public long LastGranulePos;
		}

		InfoHolderClass InfoHolder;
		bool DecodedFully;
		IEnumerator<ArraySegment<byte>> OggReader;
		Queue<byte[]> Buffer = new Queue<byte[]>();
		private bool Playing;

		protected byte[] ReadBuffer()
		{
			lock (this)
			{
				if (Buffer.Count == 0) return null;
				return Buffer.Dequeue();
			}
		}

		protected bool EnqueueBuffer()
		{
			OggReader.MoveNext();
			var Segment = OggReader.Current;
			//Segment.Array.Clone();
			if (Segment.Count > 0)
			{
				var Data = new byte[Segment.Count];
				Array.Copy(Segment.Array, Segment.Offset, Data, 0, Segment.Count);
				lock (this)
				{
					Buffer.Enqueue(Data);
				}
				return true;
			}
			else
			{
				DecodedFully = true;
				return false;
			}
		}

		protected void WaitAtLeastBuffer(int Count)
		{
			if (!DecodedFully)
			{
				while (Buffer.Count < Count)
				{
					if (DecodedFully) return;
#if MULTITHREADED
					Thread.Sleep(1);
#else
					EnqueueBuffer();
#endif
				}
			}
		}

		bool Ended;
		bool Stopped;
		public bool IsReady { get; protected set; }
		protected Thread DecoderThread;

		public OggSound(Stream OggStream)
		{
			IsReady = false;
			DecoderThread = new Thread(() =>
			{
				Debug.WriteLine("SOUND DECODER STARTED!");

				InfoHolder = new InfoHolderClass();

				if (OggStream.CanSeek)
				{
					var Data = new BinaryReader(OggStream).ReadBytes((int)OggStream.Length);
					int StartIndex = 0;
					while (true)
					{
						int Pos = Array.IndexOf(Data, (byte)'O', StartIndex);
						if (Pos < 0) break;
						if (
							(Data[Pos + 0] == 'O') &&
							(Data[Pos + 1] == 'g') &&
							(Data[Pos + 2] == 'g') &&
							(Data[Pos + 3] == 'S') &&
							(Data[Pos + 4] == '\0') &&
							((Data[Pos + 5] & 0x04) != 0)
						)
						{
							InfoHolder.LastGranulePos = Page._ReadGranulePosition(Data, Pos + 6);
							break;
						}
						StartIndex = Pos + 1;
					}
					if (InfoHolder.LastGranulePos == 0) throw (new Exception("Can't find the decoded length of the Ogg Stream (LastGranulePos)"));
					OggStream.Position = 0;
				}

				//var Stopwatch = new Stopwatch();
				//Stopwatch.Start();
				OggReader = DecodeTo(OggStream, InfoHolder).GetEnumerator();
				OggReader.MoveNext();
				if (OggReader.Current.Array != null) throw (new Exception("Invalid"));

				//WaitAtLeastBuffer(4);

				DynamicSoundEffect = new DynamicSoundEffectInstance(InfoHolder.Info.Rate, (InfoHolder.Info.Channels == 2) ? AudioChannels.Stereo : AudioChannels.Mono);
				//Debug.WriteLine("FORMAT: channels:{0}, rate:{1}", InfoHolder.Info.channels, InfoHolder.Info.rate);
				//DynamicSoundEffect = new DynamicSoundEffectInstance(22050, AudioChannels.Mono);
				DynamicSoundEffect.BufferNeeded += new EventHandler<EventArgs>(DynamicSoundEffect_BufferNeeded);
				//Debug.WriteLine("START TIME: {0}", Stopwatch.ElapsedMilliseconds);
				//FillBuffer();
				//WaitAtLeastBuffer(4);
				EnqueueBuffer();
				EnqueueBuffer();

				Debug.WriteLine("SOUND DECODER ISREADY!");

				IsReady = true;

				while (true)
				{
					if (Ended) return;
					if (Stopped) return;

					if (!EnqueueBuffer())
					{
						return;
					}
					Thread.Sleep(1);
				}
			});
			DecoderThread.Start();
		}

		DateTime PlayStartTime;

		public void Play()
		{
			Playing = true;
			DynamicSoundEffect.Play();
			PlayStartTime = DateTime.UtcNow;
		}

		public void Stop()
		{
			Playing = false;
			Stopped = true;
			DynamicSoundEffect.Stop();
		}

		private IEnumerable<ArraySegment<byte>> DecodeTo(Stream OggBaseStream, InfoHolderClass InfoHolder)
		{
			byte[] buffer;
			int bytes = 0;
			int convsize = 4096 * 2;
			var convbuffer = new byte[convsize]; // take 8k out of the data segment, not the stack
			var SyncState = new SyncState(); // sync and verify incoming physical bitstream
			var StreamState = new StreamState(); // take physical pages, weld into a logical stream of packets
			var Page = new Page(); // one Ogg bitstream page.  Vorbis packets are inside
			var Packet = new Packet(); // one raw packet of data for decode

			var Info = new Info(); // struct that stores all the static vorbis bitstream settings
			var Comment = new Comment(); // struct that stores all the bitstream user comments
			var DspState = new DspState(); // central working state for the packet->PCM decoder
			var Block = new Block(DspState); // local working space for packet->PCM decode

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
				bytes = OggBaseStream.Read(buffer, index, 4096);
				SyncState.Wrote(bytes);

				// Get the first page.
				int _result = SyncState.PageOut(Page);
				if (_result != 1)
				{
					// have we simply run out of data?  If so, we're done.
					if (bytes < 4096) break;

					Debug.WriteLine(bytes + "; " + _result);
					//File.WriteAllBytes();
					// error case.  Must not be Vorbis data
					//Debug.WriteLine("Input does not appear to be an Ogg bitstream.");
					throw (new Exception("Input does not appear to be an Ogg bitstream."));
				}

				// Get the serial number and set up the rest of decode.
				// serialno first; use it to set up a logical stream
				StreamState.Init(Page.BitStreamSerialNumber);

				// extract the initial header from the first page and verify that the
				// Ogg bitstream is in fact Vorbis data

				// I handle the initial header first instead of just having the code
				// read all three Vorbis headers at once because reading the initial
				// header is an easy way to identify a Vorbis bitstream and it's
				// useful to see that functionality seperated out.

				Info.Init();
				Comment.init();
				// error; stream version mismatch perhaps
				if (StreamState.PageIn(Page) < 0) throw (new Exception("Error reading first page of Ogg bitstream data."));
				// no page? must not be vorbis
				if (StreamState.PacketOut(Packet) != 1) throw (new Exception("Error reading initial header packet."));
				// error case; not a vorbis header
				if (Info.SynthesisHeaderIn(Comment, Packet) < 0) throw (new Exception("This Ogg bitstream does not contain Vorbis audio data."));

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
							StreamState.PageIn(Page); // we can ignore any errors here
							// as they'll also become apparent
							// at packetout
							while (i < 2)
							{
								result = StreamState.PacketOut(Packet);
								if (result == 0) break;

								// Uh oh; data at some point was corrupted or missing!
								// We can't tolerate that in a header.  Die.
								if (result == -1) throw (new Exception("Corrupt secondary header.  Exiting."));
								Info.SynthesisHeaderIn(Comment, Packet);
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
					SyncState.Wrote(bytes);
				}

				// Throw the comments plus a few lines about the bitstream we're
				// decoding
#if false
				{
					byte[][] ptr = Comment.user_comments;
					for (int j = 0; j < ptr.Length; j++)
					{
						if (ptr[j] == null) break;
						Debug.WriteLine(Util.InternalEncoding.GetString(ptr[j], 0, ptr[j].Length - 1));
					}
					Debug.WriteLine("\nBitstream is {0} channel, {1}Hz", Info.channels, Info.rate);
					Debug.WriteLine("Encoded by: {0}\n", Util.InternalEncoding.GetString(Comment.vendor, 0, Comment.vendor.Length - 1) );
				}
#endif
				convsize = 4096 / Info.Channels;
				//convsize = 4096 / Info.channels * 2;

				InfoHolder.Info = Info;
				yield return new ArraySegment<byte>();


				// OK, got and parsed all three headers. Initialize the Vorbis
				//  packet->PCM decoder.
				DspState.synthesis_init(Info); // central decode state
				Block.init(DspState); // local state for most of the decode
				// so multiple block decodes can
				// proceed in parallel.  We could init
				// multiple vorbis_block structures
				// for vd here

				float[][][] _pcm = new float[1][][];
				int[] _index = new int[Info.Channels];
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
							StreamState.PageIn(Page); // can safely ignore errors at
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
										for (i = 0; i < Info.Channels; i++)
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
												ptr += 2 * (Info.Channels);
											}
										}

										//                  System.out.write(convbuffer, 0, 2*vi.channels*bout);
										//throw(new NotImplementedException("ccccccccc"));
										//OutputBuffer.Write(convbuffer, 0, 2 * Info.channels * bout);
										InfoHolder.BufferLength = 2 * Info.Channels * convsize;
										yield return new ArraySegment<byte>(convbuffer, 0, 2 * Info.Channels * bout);

										// tell libvorbis how
										// many samples we
										// actually consumed
										DspState.synthesis_read(bout);
									}
								}
							}
							if (Page.EndOfStream) eos = true;
						}
					}

					if (!eos)
					{
						index = SyncState.Buffer(4096);
						buffer = SyncState.Data;
						bytes = OggBaseStream.Read(buffer, index, 4096);
						SyncState.Wrote(bytes);
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

				yield return new ArraySegment<byte>();
			}

			// OK, clean up the framer
			SyncState.Clear();
		}

		void FillBuffer()
		{
			/*
			int WriteBuffer = 
			WaitAtLeastBuffer(4);
			for (int n = 0; n < 4; n++)
			{
				var Segment = Buffer.Dequeue();
				//Debug.WriteLine("BUFFER: {0}, {1} : {2}:{3}", Buffer.Count, DynamicSoundEffect.PendingBufferCount, Segment.Offset, Segment.Count);
				//DynamicSoundEffect.SubmitBuffer(Segment.Array, Segment.Offset, Segment.Count);
				DynamicSoundEffect.SubmitBuffer(Segment);
			}
			*/
			int BufferCount = 2;
			do
			{
				WaitAtLeastBuffer(BufferCount);
				for (int n = 0; n < BufferCount; n++)
				{
					var Segment = ReadBuffer();
					if (Segment == null)
					{
						Ended = true;
						return;
					}
					else
					{
						EmittedBytes += Segment.Length;
						DynamicSoundEffect.SubmitBuffer(Segment);
					}
				}
			} while (DynamicSoundEffect.PendingBufferCount < BufferCount * 2);
			//DynamicSoundEffect.pen
		}

		void DynamicSoundEffect_BufferNeeded(object sender, EventArgs e)
		{
			FillBuffer();
		}

		public float Pitch
		{
			get { return DynamicSoundEffect.Pitch; }
			set { DynamicSoundEffect.Pitch = value; }
		}

		public float Volume
		{
			get { return DynamicSoundEffect.Volume; }
			set { DynamicSoundEffect.Volume = value; }
		}

		public float Pan
		{
			get { return DynamicSoundEffect.Pan; }
			set { DynamicSoundEffect.Pan = value; }
		}

		private int EmittedBytes;

		public TimeSpan Duration
		{
			get
			{
				return TimeSpan.FromSeconds(((double)InfoHolder.LastGranulePos) / (double)InfoHolder.Info.Rate);
			}
		}

		public TimeSpan Position
		{
			get
			{
				var PlayingTime2 = DateTime.UtcNow - PlayStartTime;
				if (PlayingTime2 > Duration) return Duration;
				return PlayingTime2;
			}
		}

		public void Dispose()
		{
			Ended = true;
			Stop();
		}
	}
}
