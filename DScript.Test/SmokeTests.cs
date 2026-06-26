using DScript;
using DScript.Extras;
using DScript.Extras.FunctionProviders;
using DScript.Vm;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace DScript.Test
{
    /// <summary>
    /// Synchronous smoke-test harness: 19 mini-tests run inside a single IIFE.
    /// Each test logs "✓ &lt;name&gt; &lt;result&gt;" on success or "✗ &lt;name&gt; ..." on failure.
    /// The Promise test additionally emits a second "✓ Promise 123" line after the
    /// microtask queue is drained.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class SmokeTests
    {
        private readonly List<string> _out = new();
        private readonly List<string> _err = new();

        [SetUp]
        public void SetUp()
        {
            _out.Clear();
            _err.Clear();
            ConsoleFunctionProvider.SetOutput(
                stdout: line => _out.Add(line),
                stderr: line => _err.Add(line));
        }

        [TearDown]
        public void TearDown() => ConsoleFunctionProvider.ResetOutput();

        private (List<string> lines, List<string> errors) RunSuite()
        {
            const string script = @"
(()=>{
  const t=(n,f)=>{try{const r=f();console.log(""✓"",n,r)}catch(e){console.error(""✗"",n,e)}};
  t(""Numbers"",()=>Object.is(-0,-0)&&!Object.is(-0,0)&&Number.isNaN(0/0)&&1/(-0)===-Infinity);
  t(""BigInt"",()=>2n**64n>0n);
  t(""Unicode"",()=>""😀"".length===2&&[...""😀""].length===1);
  t(""Proxy"",()=>{let x=0;const p=new Proxy({a:1},{get:(o,k)=>{x++;return Reflect.get(o,k)}});return p.a===1&&x===1});
  t(""Getter"",()=>{let x=0;const o={get a(){return ++x}};return o.a===1&&o.a===2});
  t(""Descriptors"",()=>{const o={};Object.defineProperty(o,""x"",{value:42,writable:false});try{o.x=1}catch{}return o.x===42});
  t(""Closure"",()=>{const f=x=>()=>x;return f(42)()===42});
  t(""Class"",()=>{class A{m(){return 1}}class B extends A{m(){return super.m()+1}}return new B().m()===2});
  t(""Symbol"",()=>{const s=Symbol();const o={[s]:123};return o[s]===123});
  t(""MapSet"",()=>{const m=new Map([[{},1]]),s=new Set([1,2,2]);return s.size===2&&m.size===1});
  t(""WeakMap"",()=>{const k={};const w=new WeakMap([[k,5]]);return w.get(k)===5});
  t(""TypedArray"",()=>{const b=new ArrayBuffer(8),v=new DataView(b);v.setFloat64(0,Math.PI);return Math.abs(v.getFloat64(0)-Math.PI)<1e-12});
  t(""Spread"",()=>{const o={a:1,b:2};const p={...o,c:3};return p.c===3&&p.a===1});
  t(""Destructure"",()=>{const{a,...r}={a:1,b:2};return a===1&&r.b===2});
  t(""Generator"",()=>{function*g(){yield 1;yield 2}return[...g()].join()===`1,2`});
  t(""Iterator"",()=>{let s=0;for(const x of[1,2,3])s+=x;return s===6});
  t(""Reflect"",()=>Reflect.get({x:7},""x"")===7);
  t(""Optional"",()=>({a:{b:1}}).a?.b===1&&({}).a?.b===undefined);
  t(""Promise"",()=>Promise.resolve(123).then(x=>console.log(""✓ Promise"",x)));
})();";

            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(script);
            ScriptEngine.DrainMicroTasks();
            return (_out, _err);
        }

        private string PassLineFor(string name)
        {
            var (lines, _) = RunSuite();
            return lines.FirstOrDefault(l => l.StartsWith("✓ " + name))
                   ?? $"MISSING {name}";
        }

        // ── individual tests ──────────────────────────────────────────────────

        [Test] public void Numbers_()     => Assert.That(PassLineFor("Numbers"),     Does.StartWith("✓"));
        [Test] public void BigInt_()      => Assert.That(PassLineFor("BigInt"),      Does.StartWith("✓"));
        [Test] public void Unicode_()     => Assert.That(PassLineFor("Unicode"),     Does.StartWith("✓"));
        [Test] public void Proxy_()       => Assert.That(PassLineFor("Proxy"),       Does.StartWith("✓"));
        [Test] public void Getter_()      => Assert.That(PassLineFor("Getter"),      Does.StartWith("✓"));
        [Test] public void Descriptors_() => Assert.That(PassLineFor("Descriptors"), Does.StartWith("✓"));
        [Test] public void Closure_()     => Assert.That(PassLineFor("Closure"),     Does.StartWith("✓"));
        [Test] public void Class_()       => Assert.That(PassLineFor("Class"),       Does.StartWith("✓"));
        [Test] public void Symbol_()      => Assert.That(PassLineFor("Symbol"),      Does.StartWith("✓"));
        [Test] public void MapSet_()      => Assert.That(PassLineFor("MapSet"),      Does.StartWith("✓"));
        [Test] public void WeakMap_()     => Assert.That(PassLineFor("WeakMap"),     Does.StartWith("✓"));
        [Test] public void TypedArray_()  => Assert.That(PassLineFor("TypedArray"),  Does.StartWith("✓"));
        [Test] public void Spread_()      => Assert.That(PassLineFor("Spread"),      Does.StartWith("✓"));
        [Test] public void Destructure_() => Assert.That(PassLineFor("Destructure"), Does.StartWith("✓"));
        [Test] public void Generator_()   => Assert.That(PassLineFor("Generator"),   Does.StartWith("✓"));
        [Test] public void Iterator_()    => Assert.That(PassLineFor("Iterator"),    Does.StartWith("✓"));
        [Test] public void Reflect_()     => Assert.That(PassLineFor("Reflect"),     Does.StartWith("✓"));
        [Test] public void Optional_()    => Assert.That(PassLineFor("Optional"),    Does.StartWith("✓"));

        [Test]
        public void Promise_()
        {
            var (lines, _) = RunSuite();
            var pl = lines.Where(l => l.StartsWith("✓ Promise")).ToList();
            Assert.That(pl.Count, Is.EqualTo(2), "Expected two ✓ Promise lines");
            Assert.That(pl[0], Does.Contain("[object Object]"));
            Assert.That(pl[1], Does.EndWith("123"));
        }

        [Test]
        public void AllPass()
        {
            var (_, errors) = RunSuite();
            Assert.That(errors, Is.Empty,
                errors.Count > 0
                    ? $"Smoke failures:\n{string.Join('\n', errors)}"
                    : string.Empty);
        }
    }
}
