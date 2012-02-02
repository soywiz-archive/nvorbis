using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Vorbis
{
	class Time0 : FuncTime
	{
		override internal void pack(Object i, NVorbis.Ogg.BBuffer opb)
		{
		}

		override internal Object unpack(Info vi, NVorbis.Ogg.BBuffer opb)
		{
			return "";
		}

		override internal Object look(DspState vd, InfoMode mi, Object i)
		{
			return "";
		}

		override internal void free_info(Object i)
		{
		}

		override internal void free_look(Object i)
		{
		}

		override internal int inverse(Block vb, Object i, float[] In, float[] Out)
		{
			return 0;
		}
	}
}
