using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Vorbis
{
	public class JOrbisException : Exception
	{
		private const long serialVersionUID = 1L;

		public JOrbisException()
		{
		}

		public JOrbisException(String s)
			: base("JOrbis: " + s)
		{
		}
	}
}
