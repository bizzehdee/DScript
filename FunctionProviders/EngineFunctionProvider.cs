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
	public class EngineFunctionProvider : IFunctionProvider
	{
		public void RegisterFunctions(ScriptEngine engine)
		{
			if (engine == null) return;

			String[] objectNs = { "Object" };
			String[] strNs = { "String" };
			String[] intNs = { "Integer" };

			engine.AddMethod(null, "exec", new[] { "a" }, Exec, engine);
			engine.AddMethod(null, "eval", new[] { "a" }, Eval, engine);
		}

		private static void Exec(ScriptVar var, object userdata)
		{
			ScriptEngine engine = (ScriptEngine)userdata;
			String code = var.GetParameter("a").GetString();
			engine.Execute(code);
		}

		private static void Eval(ScriptVar var, object userdata)
		{
			ScriptEngine engine = (ScriptEngine)userdata;
			String code = var.GetParameter("a").GetString();

			ScriptVarLink returnLink = engine.EvalComplex(code);
			var.SetReturnVar(returnLink.Var);
		}
	}
}
