using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Vorbis
{
#if UNSAFE
	unsafe
#endif
	public class Util
	{
		static internal int ilog(int _v)
		{
			uint v = (uint)_v;
			uint ret = 0;
			while (v != 0)
			{
				ret++;
				v >>= 1;
			}
			return (int)(ret);
		}

		static internal int ilog2(int _v)
		{
			uint v = (uint)_v;
			uint ret = 0;
			while (v > 1)
			{
				ret++;
				v >>= 1;
			}
			return (int)(ret);
		}

		static internal int icount(int _v)
		{
			uint v = (uint)_v;

			uint ret = 0;
			while (v != 0)
			{
				ret += (v & 1);
				v >>= 1;
			}
			return (int)(ret);
		}

		static internal int FloatToIntBits(float v)
		{
#if !UNSAFE
			return BitConverter.ToInt32(BitConverter.GetBytes(v), 0);
#else
			float[] vv = new float[1];
			vv[0] = v;
			fixed (float* ptr = vv)
			{
				return *(int*)ptr;
			}
#endif
		}

		static internal float IntBitsToFloat(int v)
		{
#if !UNSAFE
			return BitConverter.ToSingle(BitConverter.GetBytes(v), 0);
#else
			int[] vv = new int[1];
			vv[0] = v;
			fixed (int* ptr = vv)
			{
				return *(float*)ptr;
			}
#endif
		}

		static public Encoding InternalEncoding
		{
			get
			{
				return Encoding.UTF8;
				//return Encoding.ASCII;
			}
		}
	}

}
