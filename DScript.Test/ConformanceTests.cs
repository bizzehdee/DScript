using DScript;
using DScript.Extras;
using DScript.Vm;
using NUnit.Framework;
using System.Collections.Generic;
using System.Text;

namespace DScript.Test
{
    /// <summary>
    /// Standard JavaScript conformance suite.  Each test mirrors the async IIFE
    /// that V8 passes in full; the engine must produce "PASS &lt;name&gt;" for every case.
    ///
    /// Script source (canonical, single-line):
    /// (async()=>{let p=0,f=0;const eq=(a,b)=>{…};const test=async(n,fn,x)=>{…};
    ///   await test("NegativeZero",…); … console.log(JSON.stringify({…}));})();
    /// </summary>
    [TestFixture]
    public class ConformanceTests
    {
        // ── infrastructure ────────────────────────────────────────────────────

        private readonly List<string> _out = new();
        private readonly List<string> _err = new();

        [SetUp]
        public void SetUp()
        {
            _out.Clear();
            _err.Clear();
            DScript.Extras.FunctionProviders.ConsoleFunctionProvider.SetOutput(
                stdout: line => _out.Add(line),
                stderr: line => _err.Add(line));
        }

        [TearDown]
        public void TearDown() =>
            DScript.Extras.FunctionProviders.ConsoleFunctionProvider.ResetOutput();

        private (List<string> lines, string summary) RunSuite()
        {
            const string script = @"
(async()=>{
  let p=0,f=0;
  const eq=(a,b)=>{
    if(Object.is(a,b))return true;
    if(typeof a!==typeof b)return false;
    if(a&&b&&typeof a===""object""){
      if(Array.isArray(a)&&Array.isArray(b))return a.length===b.length&&a.every((v,i)=>eq(v,b[i]));
      if(ArrayBuffer.isView(a)&&ArrayBuffer.isView(b))return a.constructor===b.constructor&&a.length===b.length&&Array.from(a).every((v,i)=>eq(v,b[i]));
      if(a instanceof Map&&b instanceof Map){
        if(a.size!==b.size)return false;
        for(const[k,v]of a)if(!b.has(k)||!eq(v,b.get(k)))return false;
        return true;
      }
      if(a instanceof Set&&b instanceof Set){
        if(a.size!==b.size)return false;
        for(const v of a)if(!b.has(v))return false;
        return true;
      }
      const ka=Object.keys(a),kb=Object.keys(b);
      return ka.length===kb.length&&ka.every(k=>kb.includes(k)&&eq(a[k],b[k]));
    }
    return false;
  };
  const test=async(n,fn,x)=>{
    try{
      const r=await fn();
      if(eq(r,x)){console.log(""PASS"",n);p++;}
      else{console.log(""FAIL"",n,""expected:"",x,""got:"",r);f++;}
    }catch(e){console.log(""FAIL"",n,e.name,e.message);f++;}
  };
  await test(""NegativeZero"",()=>Object.is(-0,-0)&&!Object.is(-0,0),true);
  await test(""NaN"",()=>Number.isNaN(NaN)&&Object.is(NaN,NaN),true);
  await test(""Infinity"",()=>1/(-0),-Infinity);
  await test(""BigInt"",()=>2n**32n,4294967296n);
  await test(""Unicode"",()=>[...""😀𝌆""].length,2);
  await test(""JSONStringify"",()=>JSON.stringify(JSON.parse('{""a"":1,""b"":[2,3]}')),'{""a"":1,""b"":[2,3]}');
  await test(""MapOrder"",()=>[...new Map([[3,""c""],[1,""a""],[2,""b""]]).keys()],[3,1,2]);
  await test(""SetSize"",()=>new Set([1,2,2,3]).size,3);
  await test(""WeakMap"",()=>{const k={};const w=new WeakMap([[k,42]]);return w.get(k)},42);
  await test(""Symbol"",()=>{const s=Symbol();const o={[s]:99};return o[s]},99);
  await test(""ProxyGet"",()=>{let c=0;const pxy=new Proxy({x:5},{get(t,k){c++;return Reflect.get(t,k)}});return pxy.x+c},6);
  await test(""Getter"",()=>{let i=0;const o={get x(){return++i}};return o.x+o.x},3);
  await test(""Descriptor"",()=>{const o={};Object.defineProperty(o,""x"",{value:5,writable:false});try{o.x=7}catch{}return o.x},5);
  await test(""Reflect"",()=>Reflect.get({x:8},""x""),8);
  await test(""Class"",()=>{class A{v(){return 2}}class B extends A{v(){return super.v()+3}}return new B().v()},5);
  await test(""Closure"",()=>{const f=x=>()=>x+1;return f(5)()},6);
  await test(""Generator"",()=>{function*g(){yield 1;yield 2}return[...g()]},[1,2]);
  await test(""Iterator"",()=>{let s=0;for(const x of[1,2,3])s+=x;return s},6);
  await test(""Spread"",()=>({...{a:1},b:2}),{a:1,b:2});
  await test(""Destructure"",()=>{const{a,...r}={a:1,b:2};return r},{b:2});
  await test(""OptionalChain"",()=>({a:{b:5}}).a?.b??0,5);
  await test(""Nullish"",()=>null??7,7);
  await test(""RegexUnicode"",()=>/^\p{Letter}+$/u.test(""ChatGPT""),true);
  await test(""TypedArray"",()=>new Uint32Array([1,2,3]),new Uint32Array([1,2,3]));
  await test(""DataView"",()=>{const b=new ArrayBuffer(8),d=new DataView(b);d.setFloat64(0,Math.PI);return Math.abs(d.getFloat64(0)-Math.PI)<1e-12},true);
  await test(""DateUTC"",()=>new Date(Date.UTC(2000,0,1)).toISOString(),""2000-01-01T00:00:00.000Z"");
  await test(""ArrayFlat"",()=>[1,[2,[3]]].flat(2),[1,2,3]);
  await test(""ArraySort"",()=>[3,1,2].sort((a,b)=>a-b),[1,2,3]);
  await test(""StringPad"",()=>""7"".padStart(3,""0""),""007"");
  await test(""Trim"",()=>""  x \n"".trim(),""x"");
  await test(""CodePoint"",()=>String.fromCodePoint(0x1F600),""😀"");
  await test(""Promise"",()=>Promise.resolve(5),5);
  console.log(JSON.stringify({passed:p,failed:f,total:p+f}));
})();";

            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(script);
            ScriptEngine.DrainMicroTasks();

            var summary = _out.Count > 0 ? _out[^1] : "";
            return (_out, summary);
        }

        // ── individual tests ──────────────────────────────────────────────────

        private string ResultFor(string name)
        {
            var (lines, _) = RunSuite();
            foreach (var line in lines)
                if (line.StartsWith("PASS " + name) || line.StartsWith("FAIL " + name))
                    return line;
            return $"MISSING {name}";
        }

        [Test] public void NegativeZero()    => Assert.That(ResultFor("NegativeZero"),    Does.StartWith("PASS"));
        [Test] public void NaN_()            => Assert.That(ResultFor("NaN"),            Does.StartWith("PASS"));
        [Test] public void Infinity_()       => Assert.That(ResultFor("Infinity"),       Does.StartWith("PASS"));
        [Test] public void BigInt_()         => Assert.That(ResultFor("BigInt"),         Does.StartWith("PASS"));
        [Test] public void Unicode_()        => Assert.That(ResultFor("Unicode"),        Does.StartWith("PASS"));
        [Test] public void JSONStringify_()  => Assert.That(ResultFor("JSONStringify"),  Does.StartWith("PASS"));
        [Test] public void MapOrder_()       => Assert.That(ResultFor("MapOrder"),       Does.StartWith("PASS"));
        [Test] public void SetSize_()        => Assert.That(ResultFor("SetSize"),        Does.StartWith("PASS"));
        [Test] public void WeakMap_()        => Assert.That(ResultFor("WeakMap"),        Does.StartWith("PASS"));
        [Test] public void Symbol_()         => Assert.That(ResultFor("Symbol"),         Does.StartWith("PASS"));
        [Test] public void ProxyGet_()       => Assert.That(ResultFor("ProxyGet"),       Does.StartWith("PASS"));
        [Test] public void Getter_()         => Assert.That(ResultFor("Getter"),         Does.StartWith("PASS"));
        [Test] public void Descriptor_()     => Assert.That(ResultFor("Descriptor"),     Does.StartWith("PASS"));
        [Test] public void Reflect_()        => Assert.That(ResultFor("Reflect"),        Does.StartWith("PASS"));
        [Test] public void Class_()          => Assert.That(ResultFor("Class"),          Does.StartWith("PASS"));
        [Test] public void Closure_()        => Assert.That(ResultFor("Closure"),        Does.StartWith("PASS"));
        [Test] public void Generator_()      => Assert.That(ResultFor("Generator"),      Does.StartWith("PASS"));
        [Test] public void Iterator_()       => Assert.That(ResultFor("Iterator"),       Does.StartWith("PASS"));
        [Test] public void Spread_()         => Assert.That(ResultFor("Spread"),         Does.StartWith("PASS"));
        [Test] public void Destructure_()    => Assert.That(ResultFor("Destructure"),    Does.StartWith("PASS"));
        [Test] public void OptionalChain_()  => Assert.That(ResultFor("OptionalChain"),  Does.StartWith("PASS"));
        [Test] public void Nullish_()        => Assert.That(ResultFor("Nullish"),        Does.StartWith("PASS"));
        [Test] public void RegexUnicode_()   => Assert.That(ResultFor("RegexUnicode"),   Does.StartWith("PASS"));
        [Test] public void TypedArray_()     => Assert.That(ResultFor("TypedArray"),     Does.StartWith("PASS"));
        [Test] public void DataView_()       => Assert.That(ResultFor("DataView"),       Does.StartWith("PASS"));
        [Test] public void DateUTC_()        => Assert.That(ResultFor("DateUTC"),        Does.StartWith("PASS"));
        [Test] public void ArrayFlat_()      => Assert.That(ResultFor("ArrayFlat"),      Does.StartWith("PASS"));
        [Test] public void ArraySort_()      => Assert.That(ResultFor("ArraySort"),      Does.StartWith("PASS"));
        [Test] public void StringPad_()      => Assert.That(ResultFor("StringPad"),      Does.StartWith("PASS"));
        [Test] public void Trim_()           => Assert.That(ResultFor("Trim"),           Does.StartWith("PASS"));
        [Test] public void CodePoint_()      => Assert.That(ResultFor("CodePoint"),      Does.StartWith("PASS"));
        [Test] public void Promise_()        => Assert.That(ResultFor("Promise"),        Does.StartWith("PASS"));

        [Test]
        public void AllPass()
        {
            var (lines, summary) = RunSuite();
            var sb = new StringBuilder();
            foreach (var line in lines)
                if (line.StartsWith("FAIL")) sb.AppendLine(line);
            Assert.That(sb.ToString(), Is.Empty,
                $"Conformance failures:\n{sb}\nFull output:\n{string.Join('\n', lines)}");
        }
    }
}
