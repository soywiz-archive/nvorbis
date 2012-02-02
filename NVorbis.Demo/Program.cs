using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NVorbis.jorbis;
using NVorbis.jorbis.Examples;

namespace NVorbis.Demo
{
	public class Program
	{
		static public void Main(string[] Args)
		{
			DecodeExample.main(new string[] { @"..\..\..\TestInput\match0.ogg" });
			Console.ReadKey();
		}
	}
}
