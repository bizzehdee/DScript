/*
Copyright (c) 2014 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;

namespace DScript
{
	public class RNGFunctionProvider : IFunctionProvider
	{
		public void RegisterFunctions(ScriptEngine engine)
		{
			if (engine == null) return;

			String[] ns = { "Random" };

			engine.AddMethod(ns, "Next", null, Next, null);
			engine.AddMethod(ns, "NextMax", new[] { "a" }, NextMax, null);
			engine.AddMethod(ns, "NextMinMax", new[] { "a", "b" }, NextMinMax, null);
		}

		private static void Next(ScriptVar var, object userdata)
		{

			var.SetReturnVar(new ScriptVar((new Random()).Next()));
		}

		private static void NextMax(ScriptVar var, object userdata)
		{
			Int32 a = var.GetParameter("a").GetInt();

			var.SetReturnVar(new ScriptVar((new Random()).Next(a)));
		}

		private static void NextMinMax(ScriptVar var, object userdata)
		{
			Int32 a = var.GetParameter("a").GetInt();
			Int32 b = var.GetParameter("b").GetInt();

			var.SetReturnVar(new ScriptVar((new Random()).Next(a, b)));
		}


	}
}
