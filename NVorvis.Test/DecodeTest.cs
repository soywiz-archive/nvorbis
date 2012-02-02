using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVorbis.Vorbis.Examples;
using System.IO;

namespace NVorvis.Test
{
	[TestClass]
	public class DecodeTest
	{
		[TestMethod]
		public void TestDecode()
		{
			DecodeExample.main(new string[] { @"..\..\..\TestInput\match0.ogg" });

			CollectionAssert.AreEqual(
				File.ReadAllBytes(@"..\..\..\TestInput\match0.ogg.wav.ref"),
				File.ReadAllBytes(@"..\..\..\TestInput\match0.ogg.wav")
			);
		}
	}
}
