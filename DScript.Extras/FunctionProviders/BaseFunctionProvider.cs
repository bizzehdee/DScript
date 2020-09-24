using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.IO;
using System.Text;
using System.Threading;

namespace DScript.Extras.FunctionProviders
{
    public class BaseFunctionProvider
    {
        public void RegisterFunctions(ScriptEngine engine)
        {
            engine.AddNative("function eval(str)", EvalImpl, engine);
            engine.AddNative("function exec(str)", ExecImpl, engine);
            engine.AddNative("function trace()", TraceImpl, engine);
            engine.AddNative("function charToInt(char)", CharToIntImpl, null);
            engine.AddNative("function parseInt(str)", IntParseIntImpl, null);

            engine.AddNative("function Object.dump()", ObjectDumpImpl, null);
            engine.AddNative("function Object.clone()", ObjectCloneImpl, null);


            engine.AddNative("function Integer.parseInt(str)", IntParseIntImpl, null);
            engine.AddNative("function Integer.valueOf(str)", IntValueOfImpl, null);

            engine.AddNative("function JSON.stringify(obj,replacer)", JsonStringifyImpl, null);
            engine.AddNative("function JSON.parse(str)", EvalImpl, engine);

            engine.AddNative("function Array.contains(obj)", ArrayContainsImpl, null);
            engine.AddNative("function Array.remove(obj)", ArrayRemoveImpl, null);
            engine.AddNative("function Array.join(separator)", ArrayJoinImpl, null);
        }

        private void EvalImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var script = var.GetParameter("str").GetString();
            var returnVal = engine.EvalComplex(script);
            var.SetReturnVar(returnVal.Var);
        }

        private void ExecImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var script = var.GetParameter("str").GetString();
            engine.Execute(script);
        }

        private void TraceImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            engine.Root.Trace(0, null);
        }

        private void ObjectDumpImpl(ScriptVar var, object userData)
        {
            var.GetParameter("this").Trace(0, null);
        }

        private void ObjectCloneImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("this");
            var.GetReturnVar().CopyValue(obj);
        }

        private void CharToIntImpl(ScriptVar var, object userData)
        {
            var charStr = var.GetParameter("char").GetString();
            var charParam = charStr[0];
            var charAsInt = Convert.ToInt32(charParam);
            var.GetReturnVar().SetInt(charAsInt);
        }



        private void IntParseIntImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").GetString();

            if (int.TryParse(str, out int intResult) == false)
            {
                intResult = 0;
            }

            var.GetReturnVar().SetInt(intResult);
        }


        private void IntValueOfImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").GetString();

            var intResult = Convert.ToInt32(str[0]);

            var.GetReturnVar().SetInt(intResult);
        }

        private void JsonStringifyImpl(ScriptVar var, object userData)
        {
            var stream = new MemoryStream();
            var.GetParameter("obj").GetJSON(stream, "");

            stream.Seek(0, SeekOrigin.Begin);

            var streamReader = new StreamReader(stream);
            var json = streamReader.ReadToEnd();

            var.GetReturnVar().SetString(json);
        }

        private void ArrayContainsImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var v = var.GetParameter("this").FirstChild;

            bool contains = false;
            while(v != null)
            {
                if(v.Var.Equal(obj))
                {
                    contains = true;
                    break;
                }
                v = v.Next;
            }

            var.GetReturnVar().SetInt(contains ? 1 : 0);
        }

        private void ArrayRemoveImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var v = var.GetParameter("this").FirstChild;
            var removed = new List<int>();

            while(v != null)
            {
                if(v.Var.Equal(obj))
                {
                    removed.Add(v.GetIntName());
                }

                v = v.Next;
            }

            v = var.GetParameter("this").FirstChild;
            while (v != null)
            {
                var n = v.GetIntName();
                var newn = n;
                for (var i = 0; i < removed.Count; i++)
                {
                    if(n>=removed[i])
                    {
                        newn--;
                    }
                }

                if (newn != n)
                {
                    v.SetIntName(newn);
                }

                v = v.Next;
            }
        }

        private void ArrayJoinImpl(ScriptVar var, object userData)
        {
            var builder = new StringBuilder();

            var separator = var.GetParameter("separator").GetString();
            var arr = var.GetParameter("this");

            var arrayLength = arr.GetArrayLength();
            for(int x=0; x<arrayLength; x++)
            {
                if (x > 0)
                {
                    builder.Append(separator);
                }

                var str = arr.GetArrayIndex(x).GetString();
                builder.Append(str);
                
            }

            var.GetReturnVar().SetString(builder.ToString());
        }
    }
}
