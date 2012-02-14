/* -*-mode:java; c-basic-offset:2; indent-tabs-mode:nil -*- */
/* JOrbis
 * Copyright (C) 2000 ymnk, JCraft,Inc.
 *  
 * Written by: 2000 ymnk<ymnk@jcraft.com>
 *   
 * Many thanks to 
 *   Monty <monty@xiph.org> and 
 *   The XIPHOPHORUS Company http://www.xiph.org/ .
 * JOrbis has been based on their awesome works, Vorbis codec.
 *   
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Library General Public License
 * as published by the Free Software Foundation; either version 2 of
 * the License, or (at your option) any later version.
   
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Library General Public License for more details.
 * 
 * You should have received a copy of the GNU Library General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NVorbis.Ogg;

namespace NVorbis.Vorbis
{
	public class Info
	{
		private const int OV_EBADPACKET = -136;
		private const int OV_ENOTAUDIO = -135;

		private static byte[] _vorbis = Util.InternalEncoding.GetBytes("vorbis");
		private const int VI_TIMEB = 1;
		//  private static final int VI_FLOORB=1;
		private const int VI_FLOORB = 2;
		//  private static final int VI_RESB=1;
		private const int VI_RESB = 3;
		private const int VI_MAPB = 1;
		private const int VI_WINDOWB = 1;

		public int version;

		/// <summary>
		/// Number of Channels: 1 Mono, 2 Stereo
		/// </summary>
		public int Channels;

		/// <summary>
		/// Rate: 22050, 44100...
		/// </summary>
		public int Rate;

		// The below bitrate declarations are *hints*.
		// Combinations of the three values carry the following implications:
		//     
		// all three set to the same value: 
		// implies a fixed rate bitstream
		// only nominal set: 
		// implies a VBR stream that averages the nominal bitrate.  No hard 
		// upper/lower limit
		// upper and or lower set: 
		// implies a VBR bitstream that obeys the bitrate limits. nominal 
		// may also be set to give a nominal rate.
		// none set:
		//  the coder does not care to speculate.

		internal int bitrate_upper;
		internal int bitrate_nominal;
		internal int bitrate_lower;

		// Vorbis supports only short and long blocks, but allows the
		// encoder to choose the sizes

		internal int[] blocksizes = new int[2];

		// modes are the primary means of supporting on-the-fly different
		// blocksizes, different channel mappings (LR or mid-side),
		// different residue backends, etc.  Each mode consists of a
		// blocksize flag and a mapping (along with the mapping setup

		internal int Modes;
		internal int maps;
		internal int Times;
		internal int floors;
		internal int residues;
		internal int Books;
		//internal int psys; // encode only

		internal InfoMode[] ModeParam = null;

		internal int[] map_type = null;
		internal Object[] MapParam = null;

		internal int[] TimeType = null;
		internal Object[] TimeParam = null;

		internal int[] FloorType = null;
		internal Object[] FloorParam = null;

		internal int[] residue_type = null;
		internal Object[] ResidueParam = null;

		internal StaticCodeBook[] BookParam = null;

		//internal PsyInfo[] psy_param = new PsyInfo[64]; // encode only

		// for block long/sort tuning; encode only
		/*
		internal int envelopesa;
		internal float preecho_thresh;
		internal float preecho_clamp;
		*/

		// used by synthesis, which has a full, alloced vi
		public void Init()
		{
			Rate = 0;
		}

		public void Clear()
		{
			for (int i = 0; i < Modes; i++)
			{
				ModeParam[i] = null;
			}
			ModeParam = null;

			for (int i = 0; i < maps; i++)
			{ // unpack does the range checking
				FuncMapping.mapping_P[map_type[i]].free_info(MapParam[i]);
			}
			MapParam = null;

			for (int i = 0; i < Times; i++)
			{ // unpack does the range checking
				FuncTime.time_P[TimeType[i]].free_info(TimeParam[i]);
			}
			TimeParam = null;

			for (int i = 0; i < floors; i++)
			{ // unpack does the range checking
				FuncFloor.floor_P[FloorType[i]].free_info(FloorParam[i]);
			}
			FloorParam = null;

			for (int i = 0; i < residues; i++)
			{ // unpack does the range checking
				FuncResidue.residue_P[residue_type[i]].free_info(ResidueParam[i]);
			}
			ResidueParam = null;

			// the static codebooks *are* freed if you call info_clear, because
			// decode side does alloc a 'static' codebook. Calling clear on the
			// full codebook does not clear the static codebook (that's our
			// responsibility)
			for (int i = 0; i < Books; i++)
			{
				// just in case the decoder pre-cleared to save space
				if (BookParam[i] != null)
				{
					BookParam[i].clear();
					BookParam[i] = null;
				}
			}
			//if(vi->book_param)free(vi->book_param);
			BookParam = null;

			/*
			for (int i = 0; i < psys; i++)
			{
				psy_param[i].free();
			}
			*/

		}

		/// <summary>
		/// Header packing/unpacking
		/// </summary>
		/// <param name="Buffer"></param>
		/// <returns></returns>
		int UnpackInfo(NVorbis.Ogg.BBuffer Buffer)
		{
			version = Buffer.Read(32);
			if (version != 0) return (-1);

			Channels = Buffer.Read(8);
			Rate = Buffer.Read(32);

			bitrate_upper = Buffer.Read(32);
			bitrate_nominal = Buffer.Read(32);
			bitrate_lower = Buffer.Read(32);

			blocksizes[0] = 1 << Buffer.Read(4);
			blocksizes[1] = 1 << Buffer.Read(4);

			if ((Rate < 1) || (Channels < 1) || (blocksizes[0] < 8) || (blocksizes[1] < blocksizes[0])
				|| (Buffer.Read(1) != 1))
			{
				Clear();
				return (-1);
			}
			return (0);
		}

		/// <summary>
		/// All of the real encoding details are here.  The modes, books, everything.
		/// </summary>
		/// <param name="Buffer"></param>
		/// <returns></returns>
		int UnpackBooks(NVorbis.Ogg.BBuffer Buffer)
		{

			Books = Buffer.Read(8) + 1;

			if (BookParam == null || BookParam.Length != Books) BookParam = new StaticCodeBook[Books];

			for (int i = 0; i < Books; i++)
			{
				BookParam[i] = new StaticCodeBook();
				if (BookParam[i].unpack(Buffer) != 0)
				{
					Clear();
					return (-1);
				}
			}

			// time backend settings
			Times = Buffer.Read(6) + 1;
			if (TimeType == null || TimeType.Length != Times) TimeType = new int[Times];
			if (TimeParam == null || TimeParam.Length != Times) TimeParam = new Object[Times];
			for (int i = 0; i < Times; i++)
			{
				TimeType[i] = Buffer.Read(16);
				if (TimeType[i] < 0 || TimeType[i] >= VI_TIMEB)
				{
					Clear();
					return (-1);
				}
				TimeParam[i] = FuncTime.time_P[TimeType[i]].unpack(this, Buffer);
				if (TimeParam[i] == null)
				{
					Clear();
					return (-1);
				}
			}

			// floor backend settings
			floors = Buffer.Read(6) + 1;
			if (FloorType == null || FloorType.Length != floors) FloorType = new int[floors];
			if (FloorParam == null || FloorParam.Length != floors) FloorParam = new Object[floors];

			for (int i = 0; i < floors; i++)
			{
				FloorType[i] = Buffer.Read(16);
				if (FloorType[i] < 0 || FloorType[i] >= VI_FLOORB)
				{
					Clear();
					return (-1);
				}

				FloorParam[i] = FuncFloor.floor_P[FloorType[i]].unpack(this, Buffer);
				if (FloorParam[i] == null)
				{
					Clear();
					return (-1);
				}
			}

			// residue backend settings
			residues = Buffer.Read(6) + 1;

			if (residue_type == null || residue_type.Length != residues)
				residue_type = new int[residues];

			if (ResidueParam == null || ResidueParam.Length != residues)
				ResidueParam = new Object[residues];

			for (int i = 0; i < residues; i++)
			{
				residue_type[i] = Buffer.Read(16);
				if (residue_type[i] < 0 || residue_type[i] >= VI_RESB)
				{
					Clear();
					return (-1);
				}
				ResidueParam[i] = FuncResidue.residue_P[residue_type[i]].unpack(this, Buffer);
				if (ResidueParam[i] == null)
				{
					Clear();
					return (-1);
				}
			}

			// map backend settings
			maps = Buffer.Read(6) + 1;
			if (map_type == null || map_type.Length != maps)
				map_type = new int[maps];
			if (MapParam == null || MapParam.Length != maps)
				MapParam = new Object[maps];
			for (int i = 0; i < maps; i++)
			{
				map_type[i] = Buffer.Read(16);
				if (map_type[i] < 0 || map_type[i] >= VI_MAPB)
				{
					Clear();
					return (-1);
				}
				MapParam[i] = FuncMapping.mapping_P[map_type[i]].unpack(this, Buffer);
				if (MapParam[i] == null)
				{
					Clear();
					return (-1);
				}
			}

			// mode settings
			Modes = Buffer.Read(6) + 1;
			if (ModeParam == null || ModeParam.Length != Modes)
				ModeParam = new InfoMode[Modes];
			for (int i = 0; i < Modes; i++)
			{
				ModeParam[i] = new InfoMode();
				ModeParam[i].blockflag = Buffer.Read(1);
				ModeParam[i].windowtype = Buffer.Read(16);
				ModeParam[i].transformtype = Buffer.Read(16);
				ModeParam[i].mapping = Buffer.Read(8);

				if ((ModeParam[i].windowtype >= VI_WINDOWB)
					|| (ModeParam[i].transformtype >= VI_WINDOWB)
					|| (ModeParam[i].mapping >= maps))
				{
					Clear();
					return (-1);
				}
			}

			if (Buffer.Read(1) != 1)
			{
				Clear();
				return (-1);
			}

			return (0);
		}

		// The Vorbis header is in three packets; the initial small packet in
		// the first page that identifies basic parameters, a second packet
		// with bitstream comments and a third packet that holds the
		// codebook.

		public int SynthesisHeaderIn(Comment Comment, Packet Packet)
		{
			var Buffer = new NVorbis.Ogg.BBuffer();

			if (Packet != null)
			{
				Buffer.ReadInit(Packet.packet_base, Packet.packet, Packet.bytes);

				// Which of the three types of header is this?
				// Also verify header-ness, vorbis
				{
					byte[] buffer = new byte[6];
					int packtype = Buffer.Read(8);
					Buffer.Read(buffer, 6);
					if (buffer[0] != 'v' || buffer[1] != 'o' || buffer[2] != 'r' || buffer[3] != 'b'
						|| buffer[4] != 'i' || buffer[5] != 's')
					{
						// not a vorbis header
						return (-1);
					}
					switch (packtype)
					{
						case 0x01: // least significant *bit* is read first
							if (Packet.b_o_s == 0)
							{
								// Not the initial packet
								return (-1);
							}
							if (Rate != 0)
							{
								// previously initialized info header
								return (-1);
							}
							return (UnpackInfo(Buffer));
						case 0x03: // least significant *bit* is read first
							if (Rate == 0)
							{
								// um... we didn't get the initial header
								return (-1);
							}
							return (Comment.unpack(Buffer));
						case 0x05: // least significant *bit* is read first
							if (Rate == 0 || Comment.vendor == null)
							{
								// um... we didn;t get the initial header or comments yet
								return (-1);
							}
							return (UnpackBooks(Buffer));
						default:
							// Not a valid vorbis header type
							//return(-1);
							break;
					}
				}
			}
			return (-1);
		}

		// pack side
		int PackInfo(NVorbis.Ogg.BBuffer Buffer)
		{
			// preamble
			Buffer.Write(0x01, 8);
			Buffer.Write(_vorbis);

			// basic information about the stream
			Buffer.Write(0x00, 32);
			Buffer.Write(Channels, 8);
			Buffer.Write(Rate, 32);

			Buffer.Write(bitrate_upper, 32);
			Buffer.Write(bitrate_nominal, 32);
			Buffer.Write(bitrate_lower, 32);

			Buffer.Write(Util.ilog2(blocksizes[0]), 4);
			Buffer.Write(Util.ilog2(blocksizes[1]), 4);
			Buffer.Write(1, 1);
			return (0);
		}

		int PackBooks(NVorbis.Ogg.BBuffer Buffer)
		{
			Buffer.Write(0x05, 8);
			Buffer.Write(_vorbis);

			// books
			Buffer.Write(Books - 1, 8);
			for (int i = 0; i < Books; i++)
			{
				if (BookParam[i].pack(Buffer) != 0)
				{
					//goto err_out;
					return (-1);
				}
			}

			// times
			Buffer.Write(Times - 1, 6);
			for (int i = 0; i < Times; i++)
			{
				Buffer.Write(TimeType[i], 16);
				FuncTime.time_P[TimeType[i]].pack(this.TimeParam[i], Buffer);
			}

			// floors
			Buffer.Write(floors - 1, 6);
			for (int i = 0; i < floors; i++)
			{
				Buffer.Write(FloorType[i], 16);
				FuncFloor.floor_P[FloorType[i]].pack(FloorParam[i], Buffer);
			}

			// residues
			Buffer.Write(residues - 1, 6);
			for (int i = 0; i < residues; i++)
			{
				Buffer.Write(residue_type[i], 16);
				FuncResidue.residue_P[residue_type[i]].pack(ResidueParam[i], Buffer);
			}

			// maps
			Buffer.Write(maps - 1, 6);
			for (int i = 0; i < maps; i++)
			{
				Buffer.Write(map_type[i], 16);
				FuncMapping.mapping_P[map_type[i]].pack(this, MapParam[i], Buffer);
			}

			// modes
			Buffer.Write(Modes - 1, 6);
			for (int i = 0; i < Modes; i++)
			{
				Buffer.Write(ModeParam[i].blockflag, 1);
				Buffer.Write(ModeParam[i].windowtype, 16);
				Buffer.Write(ModeParam[i].transformtype, 16);
				Buffer.Write(ModeParam[i].mapping, 8);
			}
			Buffer.Write(1, 1);
			return (0);
		}

		public int BlockSize(Packet Packet)
		{
			//codec_setup_info
			var Buffer = new NVorbis.Ogg.BBuffer();

			int mode;

			Buffer.ReadInit(Packet.packet_base, Packet.packet, Packet.bytes);

			/* Check the packet type */
			if (Buffer.Read(1) != 0)
			{
				/* Oops.  This is not an audio data packet */
				return (OV_ENOTAUDIO);
			}
			{
				int modebits = 0;
				uint v = (uint)Modes;
				while (v > 1)
				{
					modebits++;
					v >>= 1;
				}

				/* read our mode and pre/post windowsize */
				mode = Buffer.Read(modebits);
			}
			if (mode == -1)
				return (OV_EBADPACKET);
			return (blocksizes[ModeParam[mode].blockflag]);
		}

		public override string ToString()
		{
			return "version:" + (version) + ", channels:" + (Channels)
				+ ", rate:" + (Rate) + ", bitrate:" + (bitrate_upper)
				+ "," + (bitrate_nominal) + "," + (bitrate_lower);
		}
	}

}
