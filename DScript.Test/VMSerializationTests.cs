using System;
using System.IO;
using DScript;
using NUnit.Framework;

namespace DScript.Test
{
    public class VMSerializationTests
    {
        [Test]
        public void TestSerializeAndDeserialize_SimpleExecution()
        {
            // Create a script that sets variables
            var code = @"
                var x = 10;
                var y = 20;
                var result = x + y;
            ";

            var engine1 = new ScriptEngine();
            engine1.Execute(code);

            // Check that the variables exist
            var resultVar = engine1.Root.FindChild("result");
            Assert.That(resultVar, Is.Not.Null);
            Assert.That(resultVar.Var.Int, Is.EqualTo(30));

            // Serialize the state
            var state = engine1.SerializeState();
            Assert.That(state, Is.Not.Null);
            Assert.That(state.RootState, Is.Not.Null);
            Assert.That(state.RootState.Length, Is.GreaterThan(0));

            // Create a new engine and deserialize
            var engine2 = new ScriptEngine();
            engine2.DeserializeState(state);

            // Verify the state was restored
            var xVar = engine2.Root.FindChild("x");
            var yVar = engine2.Root.FindChild("y");
            resultVar = engine2.Root.FindChild("result");

            Assert.That(xVar, Is.Not.Null);
            Assert.That(yVar, Is.Not.Null);
            Assert.That(resultVar, Is.Not.Null);
            Assert.That(xVar.Var.Int, Is.EqualTo(10));
            Assert.That(yVar.Var.Int, Is.EqualTo(20));
            Assert.That(resultVar.Var.Int, Is.EqualTo(30));

            // Execute new code on the restored state
            engine2.Execute("var z = x * y;");
            var zVar = engine2.Root.FindChild("z");
            Assert.That(zVar, Is.Not.Null);
            Assert.That(zVar.Var.Int, Is.EqualTo(200));
        }

        [Test]
        public void TestSerializeAndDeserialize_WithNativeFunctions()
        {
            var code = @"
                var x = 5;
                var y = 10;
            ";

            // First execution - register native function and run
            var engine1 = new ScriptEngine();
            
            engine1.AddNative("function testFunc(a, b)", (var, data) =>
            {
                var.FindChildOrCreate("return").Var.Int = 
                    var.GetParameter("a").Int + var.GetParameter("b").Int;
            }, null);

            engine1.Execute(code);

            // Verify initial state
            var xVar = engine1.Root.FindChild("x");
            Assert.That(xVar, Is.Not.Null);
            Assert.That(xVar.Var.Int, Is.EqualTo(5));
            
            // Serialize the state
            var state = engine1.SerializeState();
            Assert.That(state, Is.Not.Null);
            Assert.That(state.NativeFunctionNames, Is.Not.Null);
            Assert.That(state.NativeFunctionNames, Contains.Item("testFunc"));

            // Create a new engine and re-register the native function
            var engine2 = new ScriptEngine();
            
            engine2.AddNative("function testFunc(a, b)", (var, data) =>
            {
                var.FindChildOrCreate("return").Var.Int = 
                    var.GetParameter("a").Int + var.GetParameter("b").Int;
            }, null);

            // Deserialize the state
            engine2.DeserializeState(state);

            // Verify variables were restored
            xVar = engine2.Root.FindChild("x");
            var yVar = engine2.Root.FindChild("y");
            Assert.That(xVar, Is.Not.Null);
            Assert.That(yVar, Is.Not.Null);
            Assert.That(xVar.Var.Int, Is.EqualTo(5));
            Assert.That(yVar.Var.Int, Is.EqualTo(10));

            // Execute new code that uses the persisted variables and native function
            engine2.Execute("var z = testFunc(x, y);");
            
            var zVar = engine2.Root.FindChild("z");
            Assert.That(zVar, Is.Not.Null);
            Assert.That(zVar.Var.Int, Is.EqualTo(15));
        }

        [Test]
        public void TestSerializeDeserialize_ScriptVar()
        {
            // Create a complex variable structure
            var root = new ScriptVar(null, ScriptVar.Flags.Object);
            root.AddChild("name", new ScriptVar("Test"));
            root.AddChild("age", new ScriptVar(42));
            root.AddChild("score", new ScriptVar(98.5));
            
            var nested = new ScriptVar(null, ScriptVar.Flags.Object);
            nested.AddChild("city", new ScriptVar("London"));
            nested.AddChild("country", new ScriptVar("UK"));
            root.AddChild("location", nested);

            // Serialize
            byte[] serialized;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                root.Serialize(writer);
                writer.Flush();
                serialized = ms.ToArray();
            }

            // Deserialize
            ScriptVar deserialized;
            using (var ms = new MemoryStream(serialized))
            using (var reader = new BinaryReader(ms))
            {
                deserialized = ScriptVar.Deserialize(reader);
            }

            // Verify
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.FindChild("name").Var.String, Is.EqualTo("Test"));
            Assert.That(deserialized.FindChild("age").Var.Int, Is.EqualTo(42));
            Assert.That(deserialized.FindChild("score").Var.Float, Is.EqualTo(98.5));
            
            var locationVar = deserialized.FindChild("location");
            Assert.That(locationVar, Is.Not.Null);
            Assert.That(locationVar.Var.FindChild("city").Var.String, Is.EqualTo("London"));
            Assert.That(locationVar.Var.FindChild("country").Var.String, Is.EqualTo("UK"));
        }

        [Test]
        public void TestSerializeDeserialize_Array()
        {
            var arr = new ScriptVar(null, ScriptVar.Flags.Array);
            arr.SetArrayIndex(0, new ScriptVar(1));
            arr.SetArrayIndex(1, new ScriptVar(2));
            arr.SetArrayIndex(2, new ScriptVar(3));

            // Serialize
            byte[] serialized;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                arr.Serialize(writer);
                writer.Flush();
                serialized = ms.ToArray();
            }

            // Deserialize
            ScriptVar deserialized;
            using (var ms = new MemoryStream(serialized))
            using (var reader = new BinaryReader(ms))
            {
                deserialized = ScriptVar.Deserialize(reader);
            }

            // Verify
            Assert.That(deserialized.IsArray, Is.True);
            Assert.That(deserialized.GetArrayLength(), Is.EqualTo(3));
            Assert.That(deserialized.GetArrayIndex(0).Int, Is.EqualTo(1));
            Assert.That(deserialized.GetArrayIndex(1).Int, Is.EqualTo(2));
            Assert.That(deserialized.GetArrayIndex(2).Int, Is.EqualTo(3));
        }

        [Test]
        public void TestSerializeDeserialize_Function()
        {
            var func = new ScriptVar("{ return a + b; }", ScriptVar.Flags.Function);
            func.AddChild("a", null);
            func.AddChild("b", null);

            // Serialize
            byte[] serialized;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                func.Serialize(writer);
                writer.Flush();
                serialized = ms.ToArray();
            }

            // Deserialize
            ScriptVar deserialized;
            using (var ms = new MemoryStream(serialized))
            using (var reader = new BinaryReader(ms))
            {
                deserialized = ScriptVar.Deserialize(reader);
            }

            // Verify
            Assert.That(deserialized.IsFunction, Is.True);
            Assert.That(deserialized.FindChild("a"), Is.Not.Null);
            Assert.That(deserialized.FindChild("b"), Is.Not.Null);
            Assert.That(deserialized.String, Is.EqualTo("{ return a + b; }"));
        }

        [Test]
        public void TestDeserializeThrowsWithNullState()
        {
            var engine = new ScriptEngine();
            Assert.That(() => engine.DeserializeState(null), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void TestSerializeAndDeserialize_ComplexScenario()
        {
            // Create first engine with complex state
            var engine1 = new ScriptEngine();
            
            engine1.Execute(@"
                var user = {
                    name: 'John',
                    age: 30,
                    scores: [95, 87, 92]
                };
                var total = 0;
                for (var i = 0; i < user.scores.length; i++) {
                    total = total + user.scores[i];
                }
                user.average = total / user.scores.length;
            ");

            // Verify first engine state
            var user1 = engine1.Root.FindChild("user");
            Assert.That(user1, Is.Not.Null);
            var avg1 = user1.Var.FindChild("average");
            Assert.That(avg1, Is.Not.Null);
            Assert.That(avg1.Var.Int, Is.EqualTo(91));

            // Serialize
            var state = engine1.SerializeState();

            // Create new engine and deserialize
            var engine2 = new ScriptEngine();
            engine2.DeserializeState(state);

            // Verify state was restored
            var user2 = engine2.Root.FindChild("user");
            Assert.That(user2, Is.Not.Null);
            var name = user2.Var.FindChild("name");
            var age = user2.Var.FindChild("age");
            var avg2 = user2.Var.FindChild("average");
            
            Assert.That(name, Is.Not.Null);
            Assert.That(age, Is.Not.Null);
            Assert.That(avg2, Is.Not.Null);
            Assert.That(name.Var.String, Is.EqualTo("John"));
            Assert.That(age.Var.Int, Is.EqualTo(30));
            Assert.That(avg2.Var.Int, Is.EqualTo(91));

            // Execute more code on restored state
            engine2.Execute(@"
                user.age = user.age + 1;
                user.scores[3] = 100;
            ");

            // Verify changes
            age = user2.Var.FindChild("age");
            var scores = user2.Var.FindChild("scores");
            Assert.That(age.Var.Int, Is.EqualTo(31));
            Assert.That(scores.Var.GetArrayLength(), Is.EqualTo(4));
            Assert.That(scores.Var.GetArrayIndex(3).Int, Is.EqualTo(100));
        }
    }
}
