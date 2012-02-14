using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace NVorbis.Extra
{
#if false
	public class WaveStream
	{
		protected Stream Stream;
		protected BinaryWriter BinaryWriter;

		public WaveStream()
		{
		}

#if !UNSAFE
		[StructLayout(LayoutKind.Sequential)]
#else
		[StructLayout(LayoutKind.Sequential, Pack = 1)]
#endif
		public struct WaveFormat
		{
			/// <summary>
			/// 01 00       - For Uncompressed PCM (linear quntization)
			/// </summary>
			public ushort CompressionCode;

			/// <summary>
			/// 02 00       - Stereo
			/// </summary>
			public ushort NumberOfChannels;

			/// <summary>
			/// 44 AC 00 00 - 44100
			/// </summary>
			public uint SampleRate;

			/// <summary>
			/// Should be on uncompressed PCM : sampleRate * short.sizeof * numberOfChannels 
			/// </summary>
			public uint BytesPerSecond;

			/// <summary>
			/// short.sizeof * numberOfChannels
			/// </summary>
			public ushort BlockAlignment;

			/// <summary>
			/// ???
			/// </summary>
			public ushort BitsPerSample;

			/// <summary>
			/// 
			/// </summary>
			public ushort Padding;
		}

		protected void WriteChunk(string Name, Action Writer)
		{
			Stream.Write(Encoding.UTF8.GetBytes(Name), 0, 4);
			var SizePosition = Stream.Position;
			BinaryWriter.Write((uint)0);
			var BackPosition = Stream.Position;
			{
				Writer();
			}
			var ChunkLength = Stream.Position - BackPosition;
			var RestorePosition = Stream.Position;
			Stream.Position = SizePosition;
			BinaryWriter.Write((uint)ChunkLength);
			Stream.Position = RestorePosition;
		}

		/*
		public void WriteWave(String FileName, short[] Samples)
		{
			using (var Stream = File.Open(FileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
			{
				WriteWave(Stream, Samples);
			}
		}
		*/

		public void WriteWave(Stream Stream, Action Writer, uint NumberOfChannels = 2, uint SampleRate = 44100)
		{
			WriteWave(Stream, Writer, new WaveFormat()
			{
				CompressionCode = 1,
				SampleRate = SampleRate,
				NumberOfChannels = (ushort)NumberOfChannels,
				BytesPerSecond = 44100 * sizeof(short) * NumberOfChannels,
				BlockAlignment = (ushort)(sizeof(short) * NumberOfChannels),
				BitsPerSample = 8 * sizeof(short),
				Padding = 0,
			});
		}

		public void WriteWave(Stream Stream, Action Writer, WaveFormat WaveFormat)
		{
			this.Stream = Stream;
			this.BinaryWriter = new BinaryWriter(Stream);

			WriteChunk("RIFF", () =>
			{
				Stream.Write(Encoding.UTF8.GetBytes("WAVE"), 0, 4);
				WriteChunk("fmt ", () =>
				{
					//Stream.WriteStruct(WaveFormat);
					var BinaryWriter = new BinaryWriter(Stream);
					BinaryWriter.Write(WaveFormat.CompressionCode);
					BinaryWriter.Write(WaveFormat.NumberOfChannels);
					BinaryWriter.Write(WaveFormat.SampleRate);
					BinaryWriter.Write(WaveFormat.BytesPerSecond);
					BinaryWriter.Write(WaveFormat.BlockAlignment);
					BinaryWriter.Write(WaveFormat.BitsPerSample);
					BinaryWriter.Write(WaveFormat.Padding);
				});
				WriteChunk("data", () =>
				{
					Writer();
				});
			});
		}

		public void WriteWave(Stream Stream, short[] Samples)
		{
			WriteWave(Stream, () =>
			{
				foreach (var Sample in Samples) BinaryWriter.Write(Sample);
			});
		}
	}
#endif
}
