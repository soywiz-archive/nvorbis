using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Vorbis
{
	public class VorbisException : Exception
	{
		private const long serialVersionUID = 1L;

		public VorbisException()
		{
		}

		public VorbisException(String s)
			: base("Vorbis: " + s)
		{
		}
	}
}
