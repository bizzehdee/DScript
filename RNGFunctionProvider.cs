using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
