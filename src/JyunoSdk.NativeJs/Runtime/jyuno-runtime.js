// Jyuno Native JS 런타임 (JyunoSdk): Jyuno의 값 모델과 내장 함수를 JavaScript로 옮긴 것
//
//
// 값 표현 (Jyuno → JS)
//   null            → null
//   bool            → true / false
//   int (C# long)   → 정수인 number (±2^53 안), 그 밖의 long 범위는 BigInt
//   double          → 정수가 아닌 number(NaN, ±Infinity 포함), 정수 값인 double(3.0, -0.0)만 JDouble 상자
//                     → "정수인 number = int"라는 규칙 하나로 int/double을 구별하면서, 흔한 경우는 상자 없이 계산
//   string          → string (C#과 JS 모두 UTF-16이라 길이/인덱스/비교가 같음)
//   배열/리스트       → JList { e: 원소 타입, fixed: 배열인지, a: JS 배열 }
//   func로 만든 함수  → JS 함수 (Jyuno에서 함수는 값으로 넘길수 없으므로 변수 값과 섞이지 않음)


const U = Symbol('undefined-slot'); // 아직 만들어지지 않았거나 삭제된 변수 칸

class JDouble { constructor(v) { this.v = v; } }
class JList { constructor(e, fixed, a) { this.e = e; this.fixed = fixed; this.a = a; } }
class JConst { constructor(v) { this.v = v; } }   // (상수) = (이름) 으로 만든 상수
class Early { constructor(v) { this.v = v; } }     // 괄호 안의 식이 ')' 전에 끝남 (C#의 EarlyEnd)
const EARLY_NULL = new Early(null);
// 구조체 값 (Jyuno 1.5.0). 구조체마다 생성 코드가 이 클래스를 상속한 클래스를 만듦
// - 필드/구조체 함수는 'f_' + 이름 속성 (아직 없다면 U), $o는 만들어진 순서의 속성 이름 (생성이 끝나면 바뀌지 않으므로 복사본과 공유)
// - 프로토타입: $name(구조체 이름), $line(struct 줄, 같은 구조체인지 비교), $jn(속성 이름 → Jyuno 이름), $copy(m)(깊은 복사)
class JStruct {
  get typeName() { return this.$name; }
  fieldProps() { const r = []; for (const p of this.$o) { const v = this[p]; if (v !== U && typeof v !== 'function') r.push(p); } return r; }
  get fieldNames() { return this.fieldProps().map((p) => this.$jn[p]); }
  field(name) { for (const p of this.fieldProps()) if (this.$jn[p] === name) { const v = this[p]; return v instanceof JConst ? v.v : v; } return undefined; }
}
// 깊은 복사에서 필드 값 하나 (리스트는 새로 만들고, 리스트 안의 구조체 값은 바뀌지 않으므로 공유)
function copyValue(v, m) {
  if (typeof v !== 'object' || v === null) return v;
  if (v instanceof JStruct) return v.$copy(m);
  if (v instanceof JList) {
    const done = m.get(v);
    if (done !== undefined) return done;
    const c = new JList(v.e, v.fixed, v.a.slice());
    m.set(v, c);
    if (v.e === 'object') for (let i = 0; i < c.a.length; i++) { const x = c.a[i]; if (x instanceof JList) c.a[i] = copyValue(x, m); }
    return c;
  }
  return v;
}
// 변수, 필드, 컬렉션에 넣을 때: 구조체 값이라면 복사본
// 복사 한번 안에서 같은 리스트는 같은 복사본 (C#의 copied 사전). 복사는 사용자 코드를 실행하지 않으므로 사전 하나를 재사용
const COPIED = new Map();
function copyStruct(v) { const c = v.$copy(COPIED); if (COPIED.size !== 0) COPIED.clear(); return c; }
const own = (v) => (v instanceof JStruct ? copyStruct(v) : v);

// C#의 JyunoException 및 .NET 예외 (netType: 플레이그라운드에 보여줄 예외 타입 이름, JyunoException이면 null)
class JyunoError extends Error {
  constructor(message, netType = null) { super(message); this.netType = netType; }
}
class GrammarSignal extends Error {
  constructor(line, message) { super(message); this.line = line; }
}
class HostVar { constructor(get, set) { this.get = get; this.set = set; } }

const MAXS = Number.MAX_SAFE_INTEGER;
const BMAX = 9007199254740991n;
const LONG_MIN = -9223372036854775808n;

// BigInt를 long 범위로 순환시키고, 안전한 범위면 number로
function big2(b) {
  b = BigInt.asIntN(64, b);
  return (b <= BMAX && b >= -BMAX) ? Number(b) : b;
}
// double 연산 결과를 Jyuno double로 (정수 값이면 상자)
const D = (x) => (Number.isInteger(x) ? new JDouble(x) : x);

const isInt = (v) => (typeof v === 'number' ? Number.isInteger(v) : typeof v === 'bigint');
// Values.TryGetInteger: 정수면 number|bigint, 아니면 undefined
function tryInt(v) {
  if (typeof v === 'number') return Number.isInteger(v) ? v : undefined;
  if (typeof v === 'bigint') return v;
  return undefined;
}
// Values.TryGetReal: 숫자면 JS number, 아니면 undefined
function tryReal(v) {
  if (typeof v === 'number') return v;
  if (v instanceof JDouble) return v.v;
  if (typeof v === 'bigint') return Number(v);
  return undefined;
}

const nullError = () => new JyunoError('null을 처리할수 없습니다.');
const err = (m, t = null) => new JyunoError(m, t);

// ───────────── 문자열 변환 (Convert.ToString(value, InvariantCulture)) ─────────────

// .NET Core 3.0+의 double.ToString(): 가장 짧은 왕복 표현, 지수가 17 이상이거나 -5 이하면 과학 표기 (1E+17, 1E-05)
function fmtDouble(x) {
  if (x !== x) return 'NaN';
  if (x === Infinity) return 'Infinity';
  if (x === -Infinity) return '-Infinity';
  if (x === 0) return Object.is(x, -0) ? '-0' : '0';
  const sign = x < 0 ? '-' : '';
  const s = Math.abs(x).toExponential();
  const ei = s.indexOf('e');
  const mant = s.slice(0, ei);
  const e = +s.slice(ei + 1);
  if (e >= 17 || e <= -5) {
    const ae = Math.abs(e);
    return sign + mant + 'E' + (e < 0 ? '-' : '+') + (ae < 10 ? '0' + ae : ae);
  }
  const digits = mant.replace('.', '');
  if (e >= 0) {
    if (digits.length <= e + 1) return sign + digits + '0'.repeat(e + 1 - digits.length);
    return sign + digits.slice(0, e + 1) + '.' + digits.slice(e + 1);
  }
  return sign + '0.' + '0'.repeat(-e - 1) + digits;
}

const ELEM_NAME = { long: 'Int64', double: 'Double', string: 'String', bool: 'Boolean', object: 'Object' };
const ELEM_CS = { long: 'long', double: 'double', string: 'string', bool: 'bool', object: 'object' };
function listTypeName(l) {
  return l.fixed ? `System.${ELEM_NAME[l.e]}[]` : `System.Collections.Generic.List\`1[System.${ELEM_NAME[l.e]}]`;
}

function str(v) {
  if (typeof v === 'string') return v;
  if (typeof v === 'number') return Number.isInteger(v) ? String(v) : fmtDouble(v);
  if (v === null || v === undefined) return '';
  if (v === true) return 'True';
  if (v === false) return 'False';
  if (v instanceof JDouble) return fmtDouble(v.v);
  if (typeof v === 'bigint') return v.toString();
  if (v instanceof JList) return listTypeName(v);
  if (v instanceof JStruct) return structText(v);
  return String(v);
}
// StructValue.ToString: 'person { name = Jyunni, rating = 2424 }'
function structText(v) {
  const props = v.fieldProps();
  if (props.length === 0) return `${v.$name} { }`;
  return `${v.$name} { ${props.map((p) => { const x = v[p]; return `${v.$jn[p]} = ${show(x instanceof JConst ? x.v : x)}`; }).join(', ')} }`;
}
const show = (v) => (v === null ? 'null' : str(v)); // $"{v ?? "null"}"

// C# 컴파일러가 쓰는 타입 이름 (dynamic 연산 오류 메시지용)
function csType(v) {
  if (v === null) return '<null>';
  if (typeof v === 'number') return Number.isInteger(v) ? 'long' : 'double';
  if (typeof v === 'bigint') return 'long';
  if (v instanceof JDouble) return 'double';
  if (typeof v === 'string') return 'string';
  if (typeof v === 'boolean') return 'bool';
  if (v instanceof JList) return v.fixed ? `${ELEM_CS[v.e]}[]` : `System.Collections.Generic.List<${ELEM_CS[v.e]}>`;
  if (v instanceof JStruct) return 'Jyuno.Language.StructValue';
  return 'object';
}
// .NET 런타임 타입 이름 (형변환 오류 메시지용)
function netType(v) {
  if (typeof v === 'number') return Number.isInteger(v) ? 'System.Int64' : 'System.Double';
  if (typeof v === 'bigint') return 'System.Int64';
  if (v instanceof JDouble) return 'System.Double';
  if (typeof v === 'string') return 'System.String';
  if (typeof v === 'boolean') return 'System.Boolean';
  if (v instanceof JList) return listTypeName(v);
  return 'System.Object';
}
const binderError = (op, a, b) => err(`Operator '${op}' cannot be applied to operands of type '${csType(a)}' and '${csType(b)}'`, 'RuntimeBinderException');

// ───────────── 참/거짓 (Parser.IsTrue) ─────────────
function truthy(v) {
  if (v === true) return true;
  if (v === false || v === null) return false;
  if (typeof v === 'number') return v !== 0;
  if (typeof v === 'string') return v !== '';
  if (v instanceof JDouble) return v.v !== 0;
  return true;
}

// ───────────── 정수 연산 (long, 넘치면 순환) ─────────────
function iadd(a, b) {
  if (typeof a === 'number' && typeof b === 'number') { const r = a + b; if (r <= MAXS && r >= -MAXS) return r; }
  return big2(BigInt(a) + BigInt(b));
}
function isub(a, b) {
  if (typeof a === 'number' && typeof b === 'number') { const r = a - b; if (r <= MAXS && r >= -MAXS) return r; }
  return big2(BigInt(a) - BigInt(b));
}
function imul(a, b) {
  if (typeof a === 'number' && typeof b === 'number') { const r = a * b; if (r <= MAXS && r >= -MAXS) return r + 0; }
  return big2(BigInt(a) * BigInt(b));
}
const overflow = () => err('Arithmetic operation resulted in an overflow.', 'OverflowException');
function idiv(a, b) { // b != 0
  if (typeof a === 'number' && typeof b === 'number') return (a - a % b) / b + 0;
  a = BigInt(a); b = BigInt(b);
  if (a === LONG_MIN && b === -1n) throw overflow();
  return big2(a / b);
}
function imod(a, b) {
  if (typeof a === 'number' && typeof b === 'number') return (a % b) + 0;
  a = BigInt(a); b = BigInt(b);
  if (a === LONG_MIN && b === -1n) throw overflow();
  return big2(a % b);
}

// Values.Add / Multiply / Divide / Modulo (+ dynamic으로 계산하던 나머지 경우)
function vadd(a, b) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isInteger(a) && Number.isInteger(b)) { const r = a + b; return (r <= MAXS && r >= -MAXS) ? r : big2(BigInt(a) + BigInt(b)); }
    return D(a + b);
  }
  const ia = tryInt(a), ib = tryInt(b);
  if (ia !== undefined && ib !== undefined) return iadd(ia, ib);
  const ra = tryReal(a), rb = tryReal(b);
  if (ra !== undefined && rb !== undefined) return D(ra + rb);
  // dynamic: null과 숫자의 덧셈은 null (lifted 연산), 그 외는 오류
  if (a === null && b === null) throw err(`Operator '+=' is ambiguous on operands of type '<null>' and '<null>'`, 'RuntimeBinderException');
  if ((a === null && rb !== undefined) || (b === null && ra !== undefined)) return null;
  if (typeof a === 'string' || typeof b === 'string') return str(a) + str(b);
  throw binderError('+=', a, b);
}
function vmul(a, b) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isInteger(a) && Number.isInteger(b)) { const r = a * b; return (r <= MAXS && r >= -MAXS) ? r + 0 : big2(BigInt(a) * BigInt(b)); }
    return D(a * b);
  }
  const ia = tryInt(a), ib = tryInt(b);
  if (ia !== undefined && ib !== undefined) return imul(ia, ib);
  const ra = tryReal(a), rb = tryReal(b);
  if (ra !== undefined && rb !== undefined) return D(ra * rb);
  throw binderError('*=', a, b);
}
function vdiv(a, b) {
  const ia = tryInt(a), ib = tryInt(b);
  if (ia !== undefined && ib !== undefined) return idiv(ia, ib);
  const ra = tryReal(a), rb = tryReal(b);
  if (ra !== undefined && rb !== undefined) return D(ra / rb);
  throw binderError('/', a, b);
}
function vmod(a, b) {
  const ia = tryInt(a), ib = tryInt(b);
  if (ia !== undefined && ib !== undefined) return imod(ia, ib);
  const ra = tryReal(a), rb = tryReal(b);
  if (ra !== undefined && rb !== undefined) return D(ra % rb);
  throw binderError('%', a, b);
}

// Values.Same (equal). 구조체 값끼리 비교는 sameS (구조체가 없는 프로그램의 equal2는 이전과 같은 코드)
const sameS = (a, b) => (a instanceof JStruct ? b instanceof JStruct && a.$line === b.$line && a.$same(b) : same(a, b));
function same(a, b) {
  if (typeof a === 'number' && typeof b === 'number') return a === b;
  if (a === b) return a === a || typeof a !== 'number';
  const ia = tryInt(a), ib = tryInt(b);
  if (ia !== undefined && ib !== undefined) return ia == ib; // eslint-disable-line eqeqeq
  const ra = tryReal(a), rb = tryReal(b);
  if (ra !== undefined && rb !== undefined) return ra === rb;
  return false; // object.Equals: 문자열/논리형은 위의 a === b, 컬렉션은 참조 비교
}
// StructInstance.Equals: 같은 struct 줄에서 만들었고(같은 클래스), 필드 이름(순서 포함)과 값이 모두 같음 (배열/리스트 필드는 원소끼리)
// 클래스마다 생성 코드가 $same(b)를 만듦: sameOrder로 이름 순서를 비교한 뒤 필드마다 fieldSame
function sameOrder(a, b) {
  const oa = a.$o, ob = b.$o;
  if (oa === ob) return true; // 복사본끼리는 순서 배열을 공유
  if (oa.length === ob.length) {
    let i = 0;
    while (i < oa.length && oa[i] === ob[i]) i++;
    if (i === oa.length) return true; // 만든 순서가 같음 (필드/함수 종류가 다르면 fieldSame이 false)
  }
  const pa = a.fieldProps(), pb = b.fieldProps();
  if (pa.length !== pb.length) return false;
  for (let i = 0; i < pa.length; i++) if (pa[i] !== pb[i]) return false;
  return true;
}
function fieldSame(x, y) {
  if (typeof x === 'function' && typeof y === 'function') return true; // 구조체 함수는 필드가 아님
  if (x instanceof JConst) x = x.v;
  if (y instanceof JConst) y = y.v;
  if (x instanceof JList && y instanceof JList) {
    if (x === y) return true;
    if (x.a.length !== y.a.length) return false;
    for (let i = 0; i < x.a.length; i++) if (!fieldSame(x.a[i], y.a[i])) return false;
    return true;
  }
  return sameS(x, y);
}

// Values.Compare (less/greater): 음수/0/양수, NaN이 있으면 null
function compare(a, b) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (a !== a || b !== b) return Number.isInteger(a) && Number.isInteger(b) ? 0 : null;
    return a < b ? -1 : a > b ? 1 : 0;
  }
  const ia = tryInt(a), ib = tryInt(b);
  if (ia !== undefined && ib !== undefined) return ia < ib ? -1 : ia > ib ? 1 : 0;
  const ra = tryReal(a), rb = tryReal(b);
  if (ra !== undefined && rb !== undefined) {
    if (ra !== ra || rb !== rb) return null;
    return ra < rb ? -1 : ra > rb ? 1 : 0;
  }
  if (typeof a === 'string' && typeof b === 'string') return a < b ? -1 : a > b ? 1 : 0;
  if (typeof a === 'boolean' && typeof b === 'boolean') return a === b ? 0 : a ? 1 : -1;
  throw err(`'${show(a)}'와/과 '${show(b)}'은/는 크기를 비교할수 없습니다.`);
}

// ───────────── 컬렉션 (Collections.cs) ─────────────
function elemOf(v) {
  if (typeof v === 'number') return Number.isInteger(v) ? 'long' : 'double';
  if (typeof v === 'string') return 'string';
  if (typeof v === 'boolean') return 'bool';
  if (v instanceof JDouble) return 'double';
  if (typeof v === 'bigint') return 'long';
  return 'other';
}
function infer(items) {
  if (items.length === 0 || items[0] === null) return 'object';
  const t = elemOf(items[0]);
  if (t === 'other') return 'object';
  for (let i = 1; i < items.length; i++) if (items[i] === null || elemOf(items[i]) !== t) return 'object';
  return t;
}
const newArray = (items) => new JList(infer(items), true, items);
const newList = (items) => new JList(infer(items), false, items);

const NOFIT = Symbol('nofit');
function fit(v, e) {
  if (e === 'object') return v;
  if (v === null) return e === 'string' ? null : NOFIT;
  switch (e) {
    case 'long': return isInt(v) ? v : NOFIT;
    case 'double':
      if (typeof v === 'number') return Number.isInteger(v) ? new JDouble(v) : v;
      if (v instanceof JDouble) return v;
      if (typeof v === 'bigint') return D(Number(v));
      return NOFIT;
    case 'string': return typeof v === 'string' ? v : NOFIT;
    case 'bool': return typeof v === 'boolean' ? v : NOFIT;
  }
  return NOFIT;
}
function createLike(origin, items) {
  const out = new Array(items.length);
  for (let i = 0; i < items.length; i++) {
    const f = fit(items[i], origin.e);
    if (f === NOFIT) return origin.fixed ? newArray(items) : newList(items);
    out[i] = f;
  }
  return new JList(origin.e, origin.fixed, out);
}
function colAdd(first, args, start) {
  const items = first.a.slice();
  for (let i = start; i < args.length; i++) {
    const v = args[i];
    if (v instanceof JList) { for (const x of v.a) items.push(x); } else items.push(v);
  }
  return createLike(first, items);
}
// SameComparer와 같은 기준의 키 (NaN은 어떤 값과도 같지 않음)
function sameKey(v) {
  if (v === null) return 'z';
  if (typeof v === 'number') return v !== v ? null : 'n' + v;
  if (v instanceof JDouble) return 'n' + (v.v === 0 ? 0 : Number.isSafeInteger(v.v) ? v.v : BigInt(v.v).toString());
  if (typeof v === 'bigint') return 'n' + v.toString();
  if (typeof v === 'string') return 's' + v;
  if (typeof v === 'boolean') return 'b' + v;
  return v; // 컬렉션은 참조
}
function colSub(first, remove) {
  const all = remove instanceof JList ? remove.a : [remove];
  if (all.some((x) => x instanceof JStruct)) { // 구조체 값은 필드끼리 비교
    const items = [];
    for (const x of first.a) if (!all.some((y) => sameS(x, y))) items.push(x);
    return createLike(first, items);
  }
  const set = new Set();
  const add = (x) => { const k = sameKey(x); if (k !== null) set.add(k); };
  if (remove instanceof JList) remove.a.forEach(add); else add(remove);
  const items = [];
  for (const x of first.a) { const k = sameKey(x); if (k === null || !set.has(k)) items.push(x); }
  return createLike(first, items);
}
function indexOf(list, index) {
  const i = tryInt(index);
  if (i === undefined) throw err(`인덱스는 정수여야 합니다. ('${show(index)}')`);
  if (i < 0 || i >= list.a.length) throw err(`인덱스 ${i}은/는 범위를 벗어났습니다. (길이: ${list.a.length})`);
  return Number(i);
}
function listGet(list, index) {
  if (typeof index === 'number' && index >= 0 && index < list.a.length && Number.isInteger(index)) return list.a[index];
  return list.a[indexOf(list, index)];
}
function listSet(list, index, value) {
  const i = indexOf(list, index);
  const f = fit(value, list.e);
  if (f === NOFIT) throw err(`'${show(value)}'은/는 이 컬렉션의 원소 타입(${ELEM_NAME[list.e]})에 넣을수 없습니다.`);
  list.a[i] = own(f);
  return null;
}
// for 문의 순회 대상 (시작할 때 복사)
function forItems(v) {
  if (typeof v === 'string') { const r = new Array(v.length); for (let i = 0; i < v.length; i++) r[i] = v[i]; return r; }
  if (v instanceof JList) return v.a.slice();
  throw err(`'${show(v)}'은/는 순회할수 없습니다. (배열/리스트/문자열만 가능)`);
}
// repeat 횟수 (자연수가 아니면 0)
function repeatCount(args) {
  if (args.length === 0) return 0;
  const n = tryInt(args[0]);
  return n !== undefined && n > 0 ? Number(n) : 0;
}

// ───────────── 숫자 변환 (Values.ToInteger / ToReal) ─────────────
const INT_RE = /^[\t\n\v\f\r ]*([+-]?[0-9]+)[\t\n\v\f\r ]*$/;
function toInteger(v) {
  const i = tryInt(v);
  if (i !== undefined) return i;
  const r = tryReal(v);
  if (r !== undefined) {
    const t = Math.trunc(r);
    if (t >= -9223372036854775808 && t < 9223372036854775808) return big2(BigInt(t));
    return null;
  }
  if (typeof v === 'boolean') return v ? 1 : 0;
  const m = INT_RE.exec(str(v));
  if (!m) return null;
  const b = BigInt(m[1]);
  return b > 9223372036854775807n || b < LONG_MIN ? null : big2(b);
}
const REAL_RE = /^[\t\n\v\f\r ]*([+-]?)((?:[0-9][0-9,]*)?(?:\.[0-9]*)?)(?:[eE]([+-]?[0-9]+))?[\t\n\v\f\r ]*$/;
function toReal(v) {
  const r = tryReal(v);
  if (r !== undefined) return D(r);
  if (typeof v === 'boolean') return D(v ? 1 : 0);
  const s = str(v);
  const m = REAL_RE.exec(s);
  if (m && /[0-9]/.test(m[2]) && !m[2].startsWith(',')) return D(parseFloat(m[1] + m[2].replace(/,/g, '') + (m[3] !== undefined ? 'e' + m[3] : '')));
  const t = s.trim().toLowerCase();
  if (t === 'nan' || t === '+nan' || t === '-nan') return NaN;
  if (t === 'infinity' || t === '+infinity' || t === '∞') return Infinity;
  if (t === '-infinity' || t === '-∞') return -Infinity;
  return null;
}

// ───────────── 내장 함수 (JyunoCommands.AddDefault) ─────────────
// 인자 배열을 받는 일반형: 이름이 다시 정의될수 있는 자리나 인자 개수가 많은 호출에서 사용
const builtins = {
  int(args) { if (args.length === 0) return 0; if (args.length === 1) { if (args[0] === null) throw nullError(); return toInteger(args[0]); } return null; },
  double(args) { if (args.length === 0) return new JDouble(0); if (args.length === 1) { if (args[0] === null) throw nullError(); return toReal(args[0]); } return null; },
  string(args) { if (args.length === 0) return ''; if (args[0] === null) throw nullError(); return str(args[0]); },
  array(args) { return newArray(args.map(own)); },
  list(args) { return newList(args.map(own)); },
  length(args) {
    if (args.length === 0) throw err('길이를 구할 값이 필요합니다.');
    return length1(args[0]);
  },
  add(args) {
    if (args.length === 0) throw err('덧셈을 할 값들을 넣지 않았습니다.');
    if (args[0] instanceof JList) return colAdd(args[0], args, 1);
    if (args.length === 1) return args[0];
    for (const a of args) if (typeof a === 'string') { let s = ''; for (const x of args) s += str(x); return s; }
    let sum = args[0];
    for (let i = 1; i < args.length; i++) sum = vadd(sum, args[i]);
    return sum;
  },
  sub(args) {
    if (args.length < 2) throw err('뺄셈 할 값 2개가 필요합니다.');
    return sub2(args[0], args[1]);
  },
  mul(args) {
    if (args.length === 0) return 1;
    if (typeof args[0] === 'string') {
      if (args.length === 1) return args[0];
      return repeatString(args[0], args[1]);
    }
    let m = 1;
    for (const a of args) { if (a === null) throw nullError(); m = vmul(m, a); }
    return m;
  },
  div(args) { if (args.length < 2) throw err('나눗셈 할 값 2개가 필요합니다.'); return div2(args[0], args[1]); },
  mod(args) { if (args.length < 2) throw err('모듈로 연산 할 값 2개가 필요합니다.'); return mod2(args[0], args[1]); },
  equal(args) {
    if (args.length < 2) throw err('비교할 대상이 최소 2개 이상 있어야 합니다.');
    for (let i = 1; i < args.length; i++) if (!sameS(args[i - 1], args[i])) return false;
    return true;
  },
  less(args) { return ordered(args, 'less', -1); },
  greater(args) { return ordered(args, 'greater', 1); },
  and(args) {
    if (args.length === 0) throw err('and 연산을 할 값이 필요합니다.');
    const b = bitwise(args, 'and');
    if (b !== undefined) return b;
    for (const a of args) if (!truthy(a)) return false;
    return true;
  },
  or(args) {
    if (args.length === 0) throw err('or 연산을 할 값이 필요합니다.');
    const b = bitwise(args, 'or');
    if (b !== undefined) return b;
    for (const a of args) if (truthy(a)) return true;
    return false;
  },
  not(args) { if (args.length === 0) throw err('not 연산을 할 값이 필요합니다.'); return not1(args[0]); },
  bool(args) { return args.length > 0 && truthy(args[0]); },
};

function ordered(args, name, sign) {
  if (args.length === 0) throw err(`${name} 비교를 할 값이 필요합니다.`);
  let result = true;
  for (let i = 1; i < args.length; i++) {
    const o = compare(args[i - 1], args[i]);
    if (o === null || (sign < 0 ? !(o < 0) : !(o > 0))) result = false;
  }
  return result;
}
function bitwise(args, op) {
  let r;
  for (let i = 0; i < args.length; i++) {
    const v = tryInt(args[i]);
    if (v === undefined) return undefined;
    if (i === 0) { r = v; continue; }
    if (typeof r === 'number' && typeof v === 'number' && r === (r | 0) && v === (v | 0)) r = op === 'and' ? (r & v) : (r | v);
    else r = big2(op === 'and' ? (BigInt(r) & BigInt(v)) : (BigInt(r) | BigInt(v)));
  }
  return r;
}
function repeatString(s, count) {
  if (count === null) throw nullError();
  const n = tryInt(count);
  if (n === undefined) throw err('문자열을 반복할 횟수는 정수여야 합니다.');
  if (n <= 0) return '';
  const k = Number(n > 2147483647 ? 2147483647 : n);
  if (s.length * k > 1 << 29) throw err('Insufficient memory to continue the execution of the program.', 'OutOfMemoryException');
  return s.repeat(k);
}

// ───────────── 인자 개수가 정해진 빠른 경로 (이름이 내장 함수로 고정된 호출 자리에서 사용) ─────────────
function add2(a, b) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isInteger(a) && Number.isInteger(b)) { const r = a + b; return (r <= MAXS && r >= -MAXS) ? r : big2(BigInt(a) + BigInt(b)); }
    return D(a + b);
  }
  if (typeof a === 'string') return typeof b === 'string' ? a + b : a + str(b);
  if (a instanceof JList) return colAdd(a, [a, b], 1);
  if (typeof b === 'string') return str(a) + b;
  return vadd(a, b);
}
function sub2(a, b) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isInteger(a) && Number.isInteger(b)) { const r = a - b; return (r <= MAXS && r >= -MAXS) ? r : big2(BigInt(a) - BigInt(b)); }
    return D(a - b);
  }
  if (a instanceof JList) return colSub(a, b);
  if (typeof a === 'string') {
    if (b === null) throw nullError();
    if (typeof b !== 'string') throw err(`Unable to cast object of type '${netType(b)}' to type 'System.String'.`, 'InvalidCastException');
    if (b === '') throw err("The value cannot be an empty string. (Parameter 'oldValue')", 'ArgumentException');
    return a.split(b).join('');
  }
  const ia = tryInt(a), ib = tryInt(b);
  if (ia !== undefined && ib !== undefined) return isub(ia, ib);
  const ra = tryReal(a), rb = tryReal(b);
  if (ra !== undefined && rb !== undefined) return D(ra - rb);
  throw err('숫자 또는 문자열 이외에는 뺄셈 연산을 할수 없습니다.');
}
function mul2(a, b) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isInteger(a) && Number.isInteger(b)) { const r = a * b; return (r <= MAXS && r >= -MAXS) ? r + 0 : big2(BigInt(a) * BigInt(b)); }
    return D(a * b);
  }
  return builtins.mul([a, b]);
}
function div2(a, b) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isInteger(b)) {
      if (b === 0) throw err('0으로 나눌수 없습니다.');
      if (Number.isInteger(a)) return (a - a % b) / b + 0;
    }
    return D(a / b);
  }
  if (typeof a === 'string') {
    if (b === null) throw nullError();
    const sep = str(b);
    return new JList('string', true, sep === '' ? [a] : a.split(sep));
  }
  if (b === null) throw nullError();
  if (tryInt(b) == 0 && isInt(b)) throw err('0으로 나눌수 없습니다.'); // eslint-disable-line eqeqeq
  if (a === null) throw nullError();
  return vdiv(a, b);
}
function mod2(a, b) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isInteger(b)) {
      if (b === 0) throw err('0으로 나눌수 없습니다.');
      if (Number.isInteger(a)) return (a % b) + 0;
    }
    return D(a % b);
  }
  if (b === null) throw nullError();
  if (tryInt(b) == 0 && isInt(b)) throw err('0으로 나눌수 없습니다.'); // eslint-disable-line eqeqeq
  if (a === null) throw nullError();
  return vmod(a, b);
}
const equal2 = (a, b) => (typeof a === 'number' && typeof b === 'number' ? a === b : same(a, b));
const equalS2 = (a, b) => (typeof a === 'number' && typeof b === 'number' ? a === b : sameS(a, b)); // 구조체가 있는 프로그램
function less2(a, b) {
  if (typeof a === 'number' && typeof b === 'number') return a < b;
  const o = compare(a, b);
  return o !== null && o < 0;
}
function greater2(a, b) {
  if (typeof a === 'number' && typeof b === 'number') return a > b;
  const o = compare(a, b);
  return o !== null && o > 0;
}
function and2(a, b) {
  if (typeof a === 'number' && typeof b === 'number' && a === (a | 0) && b === (b | 0)) return a & b;
  return builtins.and([a, b]);
}
function or2(a, b) {
  if (typeof a === 'number' && typeof b === 'number' && a === (a | 0) && b === (b | 0)) return a | b;
  return builtins.or([a, b]);
}
function not1(a) {
  if (typeof a === 'number' && Number.isInteger(a)) { const r = -a - 1; return r >= -MAXS ? r : big2(~BigInt(a)); }
  if (typeof a === 'bigint') return big2(~a);
  return !truthy(a);
}
function length1(v) {
  if (typeof v === 'string') return v.length;
  if (v instanceof JList) return v.a.length;
  if (v === null) throw nullError();
  throw err(`'${str(v)}'은/는 문자열 또는 배열/리스트가 아니므로 길이를 구할수 없습니다.`);
}

// ───────────── 수학 (JyunoCommands.AddMath) ─────────────
function realArg(v) {
  if (v === null) throw nullError();
  const r = tryReal(v);
  if (r === undefined) throw err(`'${str(v)}'은/는 숫자가 아닙니다.`);
  return r;
}
function netLog(a, b) {
  if (a !== a) return a;
  if (b !== b) return b;
  if (b === 1) return NaN;
  if (a !== 1 && (b === 0 || b === Infinity)) return NaN;
  return Math.log(a) / Math.log(b);
}
const mathFunctions = {
  'math.sin': (a) => { if (!a.length) throw err('사인 값을 구할 인자가 필요합니다.'); return D(Math.sin(realArg(a[0]))); },
  'math.cos': (a) => { if (!a.length) throw err('코사인 값을 구할 인자가 필요합니다.'); return D(Math.cos(realArg(a[0]))); },
  'math.tan': (a) => { if (!a.length) throw err('탄젠트 값을 구할 인자가 필요합니다.'); return D(Math.tan(realArg(a[0]))); },
  'math.pow': (a) => { if (a.length < 2) throw err('거듭제곱에 필요한 밑과 지수가 누락되었습니다.'); return D(Math.pow(realArg(a[0]), realArg(a[1]))); },
  'math.log': (a) => {
    if (a.length < 1) throw err('로그에 필요한 진수가 누락되었습니다.');
    if (a.length === 1) return D(Math.log10(realArg(a[0])));
    return D(netLog(realArg(a[0]), realArg(a[1])));
  },
  'math.log2': (a) => { if (a.length < 1) throw err('로그에 필요한 진수가 누락되었습니다.'); return D(Math.log2(realArg(a[0]))); },
  'math.abs': (a) => {
    if (!a.length) throw err('절댓값을 구할 값이 필요합니다.');
    const v = a[0];
    if (v === null) throw nullError();
    if (typeof v === 'number') return Number.isInteger(v) ? Math.abs(v) : Math.abs(v);
    if (v instanceof JDouble) return new JDouble(Math.abs(v.v));
    if (typeof v === 'bigint') {
      if (v === LONG_MIN) throw err('Negating the minimum value of a twos complement number is invalid.', 'OverflowException');
      return big2(v < 0n ? -v : v);
    }
    throw err("The best overloaded method match for 'System.Math.Abs(short)' has some invalid arguments", 'RuntimeBinderException');
  },
};
const mathConstants = { 'math.pi': Math.PI, 'math.nan': NaN, 'math.tau': 2 * Math.PI };

// ───────────── 난수 (math.random, math.csprng, Jyuno 1.4.1) ─────────────
// 인자 규칙: (없음) → [0, 1) 실수, max → [0, max), min max → [min, max) (둘다 정수면 정수, 아니면 실수)
const TWO64 = 1n << 64n;
let cryptoSource;
function secureBytes(n) {
  if (cryptoSource === undefined) {
    cryptoSource = null;
    if (typeof globalThis !== 'undefined' && globalThis.crypto && typeof globalThis.crypto.getRandomValues === 'function') cryptoSource = globalThis.crypto;
    else if (typeof require === 'function') { try { cryptoSource = require('crypto').webcrypto; } catch (e) { cryptoSource = null; } }
  }
  if (!cryptoSource) throw err('math.csprng: 이 환경에서는 암호학적으로 안전한 난수 생성기(crypto.getRandomValues)를 쓸수 없습니다.');
  return cryptoSource.getRandomValues(new Uint32Array(n));
}
const fastSource = {
  real: () => Math.random(),
  bits64: () => (BigInt(Math.floor(Math.random() * 4294967296)) << 32n) | BigInt(Math.floor(Math.random() * 4294967296)),
};
const secureSource = {
  // 상위 53비트로 [0, 1) 실수
  real: () => { const w = secureBytes(2); return ((w[0] >>> 5) * 67108864 + (w[1] >>> 6)) / 9007199254740992; },
  bits64: () => { const w = secureBytes(2); return (BigInt(w[0]) << 32n) | BigInt(w[1]); },
};
function randomFunction(name, source) {
  const realArgOf = (v) => {
    if (v === null) throw nullError();
    const r = tryReal(v);
    if (r === undefined) throw err(`${name}: '${show(v)}'은/는 숫자가 아닙니다.`);
    return r;
  };
  const integer = (min, max) => {
    if (min >= max) throw err(`${name}: 범위가 비어있습니다. (최솟값 ${min}이/가 최댓값 ${max}보다 작아야 합니다)`);
    if (source === fastSource && typeof min === 'number' && typeof max === 'number' && max - min <= MAXS) return min + Math.floor(Math.random() * (max - min));
    const range = BigInt(max) - BigInt(min);
    // 2^64를 range로 나눈 나머지보다 작은 값을 버려서 치우침 없이 뽑음 (Jyuno와 같은 방법)
    const threshold = (TWO64 - range) % range;
    for (;;) {
      const bits = source.bits64();
      if (bits >= threshold) return big2(BigInt(min) + bits % range);
    }
  };
  const real = (min, max) => {
    if (!isFinite(min) || !isFinite(max)) throw err(`${name}: 범위는 유한한 숫자여야 합니다.`);
    if (min >= max) throw err(`${name}: 범위가 비어있습니다. (최솟값 ${fmtDouble(min)}이/가 최댓값 ${fmtDouble(max)}보다 작아야 합니다)`);
    for (;;) {
      const u = source.real();
      const r = (1 - u) * min + u * max;
      if (r >= min && r < max) return D(r);
    }
  };
  return (args) => {
    if (args.length === 0) return D(source.real());
    if (args.length === 1) {
      const max = tryInt(args[0]);
      return max !== undefined ? integer(0, max) : real(0, realArgOf(args[0]));
    }
    const min = tryInt(args[0]), max = tryInt(args[1]);
    if (min !== undefined && max !== undefined) return integer(min, max);
    return real(realArgOf(args[0]), realArgOf(args[1]));
  };
}
mathFunctions['math.random'] = randomFunction('math.random', fastSource);
mathFunctions['math.csprng'] = randomFunction('math.csprng', secureSource);

// ───────────── 컬렉션 함수 (array.*, list.*, Jyuno 1.4.1~1.4.2의 Collections.cs) ─────────────
const MAX_LENGTH = 2147483591; // C#의 Array.MaxLength
function colTarget(args, name) {
  if (args.length === 0) throw err(`${name}: 배열/리스트가 필요합니다.`);
  const v = args[0];
  if (v instanceof JList) return v;
  if (v === null) throw nullError();
  throw err(`${name}: '${show(v)}'은/는 배열/리스트가 아닙니다.`);
}
function resizable(list, name) {
  if (list.fixed) throw err(`${name}: 길이를 바꿀수 없는 컬렉션(배열 등)입니다. list로 만든 리스트를 사용하세요.`);
}
// 정수 인자 (min ~ max)
function intArg(v, what, min, max, name) {
  const i = tryInt(v);
  if (i === undefined) throw err(`${name}: ${what}은/는 정수여야 합니다. ('${show(v)}')`);
  if (i < min || i > max) throw err(`${name}: ${what} ${i}은/는 범위(${min} ~ ${max})를 벗어났습니다.`);
  return Number(i);
}
function fitOrThrow(list, v, name) {
  const f = fit(v, list.e);
  if (f === NOFIT) throw err(`${name}: '${show(v)}'은/는 이 컬렉션의 원소 타입(${ELEM_NAME[list.e]})에 넣을수 없습니다.`);
  return f;
}
// sort의 비교: less/greater와 같은 기준, NaN은 가장 앞
function sortCompare(a, b) {
  const o = compare(a, b);
  if (o !== null) return o;
  const ra = tryReal(a), rb = tryReal(b);
  const na = ra !== undefined && ra !== ra, nb = rb !== undefined && rb !== rb;
  return na === nb ? 0 : na ? -1 : 1;
}
// 안정 정렬 (Jyuno와 같은 병합 정렬: 비교 순서가 같아야 비교할수 없는 값의 오류 메시지도 같음)
function mergeSort(items, buffer, start, end, sign) {
  if (end - start < 2) return;
  const middle = (start + end) >> 1;
  mergeSort(items, buffer, start, middle, sign);
  mergeSort(items, buffer, middle, end, sign);
  if (sign * sortCompare(items[middle - 1], items[middle]) <= 0) return;
  for (let i = start; i < end; i++) buffer[i] = items[i];
  let left = start, right = middle, at = start;
  while (left < middle && right < end) items[at++] = sign * sortCompare(buffer[right], buffer[left]) < 0 ? buffer[right++] : buffer[left++];
  while (left < middle) items[at++] = buffer[left++];
  while (right < end) items[at++] = buffer[right++];
}
function colIndexOf(args, name) {
  const list = colTarget(args, name);
  if (args.length < 2) throw err(`${name}: 찾을 값이 필요합니다.`);
  const a = list.a;
  for (let i = 0; i < a.length; i++) if (sameS(a[i], args[1])) return i;
  return -1;
}
function colRange(args, array, name) {
  if (args.length === 0) throw err(`${name}: 범위가 필요합니다.`);
  const integer = (i, what) => {
    const v = tryInt(args[i]);
    if (v === undefined) throw err(`${name}: ${what}은/는 정수여야 합니다. ('${show(args[i])}')`);
    return BigInt(v);
  };
  let start = 0n, end, step = 1n;
  if (args.length === 1) end = integer(0, '끝');
  else {
    start = integer(0, '시작');
    end = integer(1, '끝');
    if (args.length > 2) step = integer(2, '간격');
  }
  if (step === 0n) throw err(`${name}: 간격은 0이 될수 없습니다.`);
  let count = 0n;
  if (step > 0n && start < end) count = (end - start - 1n) / step + 1n;
  else if (step < 0n && start > end) count = (start - end - 1n) / -step + 1n;
  if (count > BigInt(MAX_LENGTH)) throw err(`${name}: 원소가 너무 많습니다. (${count}개)`);
  const n = Number(count), items = new Array(n);
  const last = start + (count - 1n) * step;
  if (n > 0 && start >= -BMAX && start <= BMAX && last >= -BMAX && last <= BMAX) {
    const s = Number(start), d = Number(step);
    for (let i = 0; i < n; i++) items[i] = s + i * d;
  } else {
    for (let i = 0; i < n; i++) items[i] = big2(start + BigInt(i) * step);
  }
  return new JList('long', array, items);
}
function addCollectionBuiltins() {
  for (const kind of ['array', 'list']) {
    const array = kind === 'array';
    const f = (n, fn) => { const name = `${kind}.${n}`; builtins[name] = (args) => fn(args, name); };
    f('sort', (args, name) => {
      const list = colTarget(args, name);
      const sign = args.length > 1 && truthy(args[1]) ? -1 : 1;
      const items = list.a.slice();
      mergeSort(items, new Array(items.length), 0, items.length, sign);
      for (let i = 0; i < items.length; i++) list.a[i] = items[i];
      return list;
    });
    f('reverse', (args, name) => { const list = colTarget(args, name); list.a.reverse(); return list; });
    f('shuffle', (args, name) => {
      const list = colTarget(args, name);
      const a = list.a;
      for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); const t = a[i]; a[i] = a[j]; a[j] = t; }
      return list;
    });
    f('contains', (args, name) => colIndexOf(args, name) >= 0);
    f('indexof', colIndexOf);
    f('slice', (args, name) => {
      const list = colTarget(args, name);
      if (args.length < 2) throw err(`${name}: 시작 인덱스가 필요합니다.`);
      const len = list.a.length;
      const start = intArg(args[1], '시작 인덱스', 0, len, name);
      const end = args.length > 2 ? intArg(args[2], '끝 인덱스', start, len, name) : len;
      return createLike(list, list.a.slice(start, end));
    });
    f('join', (args, name) => {
      const list = colTarget(args, name);
      const separator = args.length > 1 ? str(args[1]) : '';
      return list.a.map(str).join(separator);
    });
    f('new', (args, name) => {
      if (args.length === 0) throw err(`${name}: 길이가 필요합니다.`);
      const length = intArg(args[0], '길이', 0, MAX_LENGTH, name);
      const items = new Array(length).fill(args.length > 1 ? own(args[1]) : null);
      return array ? newArray(items) : newList(items);
    });
    // 1.4.2
    f('copy', (args, name) => { const list = colTarget(args, name); return createLike(list, list.a.slice()); });
    f('lastindexof', (args, name) => {
      const list = colTarget(args, name);
      if (args.length < 2) throw err(`${name}: 찾을 값이 필요합니다.`);
      const a = list.a;
      for (let i = a.length - 1; i >= 0; i--) if (sameS(a[i], args[1])) return i;
      return -1;
    });
    f('count', (args, name) => {
      const list = colTarget(args, name);
      if (args.length < 2) throw err(`${name}: 셀 값이 필요합니다.`);
      let n = 0;
      for (const x of list.a) if (sameS(x, args[1])) n++;
      return n;
    });
    f('distinct', (args, name) => {
      const list = colTarget(args, name);
      if (list.a.some((x) => x instanceof JStruct)) { // 구조체 값은 필드끼리 비교 (키를 만들수 없으므로 하나씩)
        const items = [];
        for (const x of list.a) if (!items.some((y) => sameS(y, x))) items.push(x); // NaN은 어떤 값과도 같지 않음
        return createLike(list, items);
      }
      const seen = new Set(), items = [];
      for (const x of list.a) {
        const k = sameKey(x);
        if (k === null) { items.push(x); continue; } // NaN은 어떤 값과도 같지 않음
        if (!seen.has(k)) { seen.add(k); items.push(x); }
      }
      return createLike(list, items);
    });
    f('binary_search', (args, name) => {
      const list = colTarget(args, name);
      if (args.length < 2) throw err(`${name}: 찾을 값이 필요합니다.`);
      const a = list.a;
      let low = 0, high = a.length;
      while (low < high) {
        const middle = low + ((high - low) >> 1);
        if (sortCompare(a[middle], args[1]) < 0) low = middle + 1; else high = middle;
      }
      if (low < a.length && sortCompare(a[low], args[1]) === 0) return low;
      return -low - 1;
    });
    f('fill', (args, name) => {
      const list = colTarget(args, name);
      const v = own(fitOrThrow(list, args.length > 1 ? args[1] : null, name));
      list.a.fill(v);
      return list;
    });
    const extreme = (sign) => (args, name) => {
      const list = colTarget(args, name);
      const a = list.a;
      if (a.length === 0) throw err(`${name}: 빈 컬렉션입니다.`);
      let best = a[0];
      for (let i = 1; i < a.length; i++) if (sign * sortCompare(a[i], best) > 0) best = a[i];
      return best;
    };
    f('min', extreme(-1));
    f('max', extreme(1));
    f('sum', (args, name) => {
      const list = colTarget(args, name);
      let sum = 0;
      for (const x of list.a) {
        if (tryReal(x) === undefined) throw err(`${name}: '${show(x)}'은/는 숫자가 아닙니다.`);
        sum = vadd(sum, x);
      }
      return sum;
    });
    f('range', (args, name) => colRange(args, array, name));
  }
  const l = (n, fn) => { const name = `list.${n}`; builtins[name] = (args) => fn(args, name); };
  l('push', (args, name) => {
    const list = colTarget(args, name);
    resizable(list, name);
    const values = [];
    for (let i = 1; i < args.length; i++) values.push(own(fitOrThrow(list, args[i], name)));
    for (const v of values) list.a.push(v);
    return list;
  });
  l('pop', (args, name) => {
    const list = colTarget(args, name);
    resizable(list, name);
    if (list.a.length === 0) throw err(`${name}: 빈 리스트에서 원소를 꺼낼수 없습니다.`);
    return list.a.pop();
  });
  l('insert', (args, name) => {
    const list = colTarget(args, name);
    resizable(list, name);
    if (args.length < 3) throw err(`${name}: 리스트, 인덱스, 값이 필요합니다.`);
    const at = intArg(args[1], '인덱스', 0, list.a.length, name);
    list.a.splice(at, 0, own(fitOrThrow(list, args[2], name)));
    return list;
  });
  l('removeat', (args, name) => {
    const list = colTarget(args, name);
    resizable(list, name);
    if (args.length < 2) throw err(`${name}: 제거할 인덱스가 필요합니다.`);
    if (list.a.length === 0) throw err(`${name}: 빈 리스트에서 원소를 제거할수 없습니다.`);
    const at = intArg(args[1], '인덱스', 0, list.a.length - 1, name);
    return list.a.splice(at, 1)[0];
  });
  l('remove', (args, name) => {
    const list = colTarget(args, name);
    resizable(list, name);
    const at = colIndexOf(args, name);
    if (at < 0) return false;
    list.a.splice(at, 1);
    return true;
  });
  l('clear', (args, name) => {
    const list = colTarget(args, name);
    resizable(list, name);
    list.a.length = 0;
    return list;
  });
}
addCollectionBuiltins();

// ───────────── 문자열 함수 (string.*, Jyuno 1.4.2의 Strings.cs) ─────────────
// 찾기/비교는 서수(UTF-16 코드) 기준: JS의 문자열 비교/indexOf와 같음
// 공백 문자: C#의 char.IsWhiteSpace (JS의 \s와 다름: U+0085 포함, U+FEFF 제외)
const WHITESPACE = new Set([0x9, 0xa, 0xb, 0xc, 0xd, 0x20, 0x85, 0xa0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000]);
const WHITESPACE_RE = /[\t\n\v\f\r \u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]/;
// C#의 ToUpperInvariant/ToLowerInvariant: 한 글자를 한 글자로 바꾸는 단순 대소문자 변환
// (JS의 toUpperCase는 'ß' → 'SS'처럼 길이가 바뀌는 변환도 하므로, 한 글자로 바뀌는 경우만 사용하고 .NET과 다른 글자는 따로 지정)
const UPPER_FIX = new Map([[0x131, 0x131], [0x17f, 0x17f], [0x1fb3, 0x1fbc], [0x1fc3, 0x1fcc], [0x1ff3, 0x1ffc]]);
for (const base of [0x1f80, 0x1f90, 0x1fa0]) for (let i = 0; i < 8; i++) UPPER_FIX.set(base + i, base + 8 + i);
function invariantCase(s, upper) {
  if (/^[\x00-\x7f]*$/.test(s)) return upper ? s.toUpperCase() : s.toLowerCase();
  let out = '';
  for (const ch of s) {
    if (upper) {
      const fixed = UPPER_FIX.get(ch.codePointAt(0));
      if (fixed !== undefined) { out += String.fromCodePoint(fixed); continue; }
    }
    const m = upper ? ch.toUpperCase() : ch.toLowerCase();
    out += m.length === ch.length && m.codePointAt(0) !== undefined && String.fromCodePoint(m.codePointAt(0)) === m ? m : ch;
  }
  return out;
}
function addStringBuiltins() {
  const need = (args, n, name) => { if (args.length < n) throw err(`${name}: 인자가 ${n}개 이상 필요합니다.`); };
  const text = (args, i, name) => {
    const v = args[i];
    if (typeof v === 'string') return v;
    if (v === null) throw nullError();
    throw err(`${name}: '${show(v)}'은/는 문자열이 아닙니다.`);
  };
  const textOrNull = (args, i, name) => (args[i] === null ? null : text(args, i, name));
  const trimBy = (s, set, start, end) => {
    let a = 0, b = s.length;
    if (start) while (a < b && set.has(s.charCodeAt(a))) a++;
    if (end) while (b > a && set.has(s.charCodeAt(b - 1))) b--;
    return s.slice(a, b);
  };
  const trim = (start, end) => (args, name) => {
    need(args, 1, name);
    const s = text(args, 0, name);
    const chars = args.length > 1 ? text(args, 1, name) : null;
    let set = WHITESPACE;
    if (chars !== null && chars.length > 0) { set = new Set(); for (let i = 0; i < chars.length; i++) set.add(chars.charCodeAt(i)); }
    return trimBy(s, set, start, end);
  };
  const pad = (left) => (args, name) => {
    need(args, 2, name);
    const s = text(args, 0, name);
    const width = intArg(args[1], '길이', 0, MAX_LENGTH, name);
    let fill = ' ';
    if (args.length > 2) {
      const c = text(args, 2, name);
      if (c.length !== 1) throw err(`${name}: 채울 글자는 한 글자여야 합니다. ('${c}')`);
      fill = c;
    }
    return left ? s.padStart(width, fill) : s.padEnd(width, fill);
  };
  const functions = {
    concat: (args) => args.map(str).join(''),
    join: (args, name) => {
      need(args, 1, name);
      const separator = str(args[0]);
      if (args.length === 2 && args[1] instanceof JList) return args[1].a.map(str).join(separator);
      return args.slice(1).map(str).join(separator);
    },
    format: (args, name) => {
      need(args, 1, name);
      const format = text(args, 0, name);
      try { return composite(format, args.slice(1)); } catch (e) {
        if (e instanceof FormatError) throw err(`${name}: 형식 문자열이 올바르지 않습니다: "${format}"`);
        throw e;
      }
    },
    compare: (args, name) => {
      need(args, 2, name);
      const a = textOrNull(args, 0, name), b = textOrNull(args, 1, name);
      if (a === b) return 0;
      if (a === null) return -1;
      if (b === null) return 1;
      return a < b ? -1 : 1;
    },
    equals: (args, name) => {
      need(args, 2, name);
      const a = textOrNull(args, 0, name), b = textOrNull(args, 1, name);
      if (args.length > 2 && truthy(args[2])) return a === null || b === null ? a === b : invariantCase(a, true) === invariantCase(b, true);
      return a === b;
    },
    isnullorempty: (args, name) => { need(args, 1, name); const s = textOrNull(args, 0, name); return s === null || s === ''; },
    isnullorwhitespace: (args, name) => {
      need(args, 1, name);
      const s = textOrNull(args, 0, name);
      if (s === null) return true;
      for (let i = 0; i < s.length; i++) if (!WHITESPACE.has(s.charCodeAt(i))) return false;
      return true;
    },
    indexof: (args, name) => {
      need(args, 2, name);
      const s = text(args, 0, name), value = text(args, 1, name);
      const start = args.length > 2 ? intArg(args[2], '시작 인덱스', 0, s.length, name) : 0;
      return s.indexOf(value, start);
    },
    lastindexof: (args, name) => { need(args, 2, name); return text(args, 0, name).lastIndexOf(text(args, 1, name)); },
    contains: (args, name) => { need(args, 2, name); return text(args, 0, name).includes(text(args, 1, name)); },
    startswith: (args, name) => { need(args, 2, name); return text(args, 0, name).startsWith(text(args, 1, name)); },
    endswith: (args, name) => { need(args, 2, name); return text(args, 0, name).endsWith(text(args, 1, name)); },
    replace: (args, name) => {
      need(args, 3, name);
      const s = text(args, 0, name), old = text(args, 1, name);
      if (old.length === 0) throw err(`${name}: 바꿀 문자열은 빈 문자열일수 없습니다.`);
      return s.split(old).join(textOrNull(args, 2, name) ?? '');
    },
    substring: (args, name) => {
      need(args, 2, name);
      const s = text(args, 0, name);
      const start = intArg(args[1], '시작 인덱스', 0, s.length, name);
      const length = args.length > 2 ? intArg(args[2], '길이', 0, s.length - start, name) : s.length - start;
      return s.slice(start, start + length);
    },
    insert: (args, name) => {
      need(args, 3, name);
      const s = text(args, 0, name);
      const at = intArg(args[1], '인덱스', 0, s.length, name);
      return s.slice(0, at) + text(args, 2, name) + s.slice(at);
    },
    remove: (args, name) => {
      need(args, 2, name);
      const s = text(args, 0, name);
      const start = intArg(args[1], '시작 인덱스', 0, s.length, name);
      const count = args.length > 2 ? intArg(args[2], '개수', 0, s.length - start, name) : s.length - start;
      return s.slice(0, start) + s.slice(start + count);
    },
    toupper: (args, name) => { need(args, 1, name); return invariantCase(text(args, 0, name), true); },
    tolower: (args, name) => { need(args, 1, name); return invariantCase(text(args, 0, name), false); },
    trim: trim(true, true),
    trimstart: trim(true, false),
    trimend: trim(false, true),
    padleft: pad(true),
    padright: pad(false),
    split: (args, name) => {
      need(args, 1, name);
      const s = text(args, 0, name);
      if (args.length === 1) return new JList('string', true, s.split(WHITESPACE_RE));
      const separator = text(args, 1, name);
      return new JList('string', true, separator === '' ? [s] : s.split(separator));
    },
    tochararray: (args, name) => { need(args, 1, name); return new JList('string', true, text(args, 0, name).split('')); },
    charcode: (args, name) => {
      need(args, 1, name);
      const s = text(args, 0, name);
      if (s.length === 0) throw err(`${name}: 빈 문자열입니다.`);
      const at = args.length > 1 ? intArg(args[1], '인덱스', 0, s.length - 1, name) : 0;
      return s.charCodeAt(at);
    },
    fromcharcode: (args, name) => {
      let out = '';
      for (const v of args) out += String.fromCharCode(intArg(v, '문자 코드', 0, 65535, name));
      return out;
    },
  };
  for (const [n, fn] of Object.entries(functions)) { const name = `string.${n}`; builtins[name] = (args) => fn(args, name); }
}
addStringBuiltins();

// ───────────── 복합 서식 (string.Format) ─────────────
class FormatError extends Error {}
function composite(format, args) {
  let out = '';
  const n = format.length;
  let i = 0;
  while (i < n) {
    const c = format[i];
    if (c === '}') {
      if (format[i + 1] === '}') { out += '}'; i += 2; continue; }
      throw new FormatError();
    }
    if (c !== '{') { out += c; i++; continue; }
    if (format[i + 1] === '{') { out += '{'; i += 2; continue; }
    i++;
    let idx = '';
    while (i < n && format[i] >= '0' && format[i] <= '9') idx += format[i++];
    if (idx === '') throw new FormatError();
    while (i < n && format[i] === ' ') i++;
    let align = 0;
    if (format[i] === ',') {
      i++;
      while (i < n && format[i] === ' ') i++;
      let a = '';
      if (format[i] === '-') a += format[i++];
      while (i < n && format[i] >= '0' && format[i] <= '9') a += format[i++];
      if (a === '' || a === '-') throw new FormatError();
      align = parseInt(a, 10);
      while (i < n && format[i] === ' ') i++;
    }
    let spec = null;
    if (format[i] === ':') {
      i++;
      spec = '';
      while (i < n && format[i] !== '}') {
        if (format[i] === '{') throw new FormatError();
        spec += format[i++];
      }
    }
    if (format[i] !== '}') throw new FormatError();
    i++;
    const k = parseInt(idx, 10);
    if (k >= args.length) throw new FormatError();
    let s = formatValue(args[k], spec);
    if (Math.abs(align) > s.length) s = align > 0 ? s.padStart(align) : s.padEnd(-align);
    out += s;
  }
  return out;
}
function group(intDigits) { return intDigits.replace(/\B(?=(\d{3})+(?!\d))/g, ','); }
function formatValue(v, spec) {
  if (v === null) return '';
  const isNum = typeof v === 'number' || typeof v === 'bigint' || v instanceof JDouble;
  if (!spec || !isNum) return str(v);
  const integer = isInt(v);
  const m = /^([A-Za-z])(\d{0,2})$/.exec(spec);
  if (m) {
    const f = m[1], p = m[2] === '' ? null : +m[2];
    const x = integer ? Number(v) : tryReal(v);
    switch (f.toUpperCase()) {
      case 'D':
        if (!integer) break;
        { const neg = v < 0; const d = (neg ? -BigInt(v) : BigInt(v)).toString(); return (neg ? '-' : '') + (p ? d.padStart(p, '0') : d); }
      case 'X':
        if (!integer) break;
        { let h = BigInt.asUintN(64, BigInt(v)).toString(16); if (f === 'X') h = h.toUpperCase(); return p ? h.padStart(p, '0') : h; }
      case 'F': return fixedOf(x, p ?? 2, false);
      case 'N': return fixedOf(x, p ?? 2, true);
      case 'P': return fixedOf(x * 100, p ?? 2, true) + ' %';
      case 'E': {
        const s = x.toExponential(p ?? 6);
        const [mt, ex] = s.split('e');
        const e = +ex;
        return mt + (f === 'e' ? 'e' : 'E') + (e < 0 ? '-' : '+') + String(Math.abs(e)).padStart(3, '0');
      }
      case 'G': case 'R': if (p === null || f.toUpperCase() === 'R') return str(v); break;
    }
    throw new JyunoError(`Native JS: 형식 '${spec}'은/는 아직 지원하지 않습니다.`);
  }
  const cm = /^([#0,]*)(?:\.([#0]*))?$/.exec(spec);
  if (cm) {
    const x = integer ? Number(v) : tryReal(v);
    const ip = cm[1], fp = cm[2] ?? '';
    const minFrac = (fp.match(/0/g) || []).length, maxFrac = fp.length;
    let s = Math.abs(x).toFixed(maxFrac);
    if (maxFrac > minFrac) s = s.replace(new RegExp(`0{1,${maxFrac - minFrac}}$`), '').replace(/\.$/, '');
    let [ints, frac] = s.split('.');
    const minInt = (ip.match(/0/g) || []).length;
    if (ints === '0' && minInt === 0) ints = '';
    ints = ints.padStart(minInt, '0');
    if (ip.includes(',')) ints = group(ints);
    const body = frac ? `${ints}.${frac}` : ints;
    return (x < 0 && /[1-9]/.test(body) ? '-' : '') + body;
  }
  throw new JyunoError(`Native JS: 형식 '${spec}'은/는 아직 지원하지 않습니다.`);
}
function fixedOf(x, p, grouping) {
  if (x !== x) return 'NaN';
  if (!isFinite(x)) return x > 0 ? 'Infinity' : '-Infinity';
  let s = Math.abs(x).toFixed(p);
  if (grouping) { const [a, b] = s.split('.'); s = group(a) + (b !== undefined ? '.' + b : ''); }
  return (x < 0 && /[1-9]/.test(s) ? '-' : '') + s;
}
function formatArgs(args) {
  if (typeof args[0] !== 'string') throw err('형식 문자열은 문자열이어야 합니다.');
  try { return composite(args[0], args); } catch (e) {
    if (e instanceof FormatError) throw err(`형식 문자열이 올바르지 않습니다: "${args[0]}"`);
    throw e;
  }
}

// 플레이그라운드 결과 표시 (JyunoSandbox.Describe / TypeName)
function describe(v) {
  if (v === null) return 'null';
  if (typeof v === 'string') return JSON.stringify(v);
  if (typeof v === 'boolean') return v ? 'true' : 'false';
  return str(v);
}
function typeName(v) {
  if (v === null) return 'null';
  if (isInt(v)) return 'int';
  if (typeof v === 'number' || v instanceof JDouble) return 'double';
  if (typeof v === 'string') return 'string';
  if (typeof v === 'boolean') return 'bool';
  if (v instanceof JList) return v.fixed ? `${ELEM_NAME[v.e]}[]` : 'List`1';
  return 'object';
}

// ───────────── 인자 3개의 빠른 경로 (Jyuno 1.3.4의 호출 자리 특화와 같은 범위) ─────────────
function add3(a, b, c) { return typeof a === 'number' && typeof b === 'number' && typeof c === 'number' ? add2(add2(a, b), c) : builtins.add([a, b, c]); }
function mul3(a, b, c) { return typeof a === 'number' && typeof b === 'number' && typeof c === 'number' ? mul2(mul2(a, b), c) : builtins.mul([a, b, c]); }

// ───────────── 표준 호스트: 콘솔과 수학 (Jyuno의 AddConsole/AddMath, JyunoWeb의 JyunoHost) ─────────────
// System.IO.StringReader처럼 줄/글자를 읽음
class StringReader {
  constructor(s) { this.s = s; this.p = 0; }
  readLine() {
    const s = this.s;
    if (this.p >= s.length) return null;
    let i = this.p;
    while (i < s.length && s[i] !== '\n' && s[i] !== '\r') i++;
    const line = s.slice(this.p, i);
    if (i < s.length && s[i] === '\r' && s[i + 1] === '\n') i++;
    this.p = i + 1;
    return line;
  }
  read() { return this.p < this.s.length ? this.s.charCodeAt(this.p++) : -1; }
}
const textOf = (v) => (v === null ? null : str(v));
function addConsole(host, io) {
  host.set('console.write', { kind: 'func', fn: (a) => { if (a.length === 0) return null; io.write(a.length === 1 ? textOf(a[0]) ?? 'null' : formatArgs(a)); return null; } });
  host.set('console.writeline', { kind: 'func', fn: (a) => {
    if (a.length === 0) io.write('\n');
    else if (a.length === 1) io.write((textOf(a[0]) ?? '') + '\n');
    else io.write(formatArgs(a) + '\n');
    return null;
  } });
  host.set('console.readline', { kind: 'func', fn: () => io.readLine() });
  host.set('console.read', { kind: 'func', fn: () => { const c = io.read(); return String.fromCharCode(c < 0 ? 0xffff : c); } });
  host.set('console.clear', { kind: 'func', fn: () => { io.clear(); return null; } });
  let title = 'Jyuno';
  host.set('console.title', { kind: 'var', get: () => title, set: (v) => { const t = textOf(v); if (t === null) throw nullError(); title = t; io.title(t); } });
}
function addMath(host) {
  for (const k of Object.keys(mathFunctions)) host.set(k, { kind: 'func', fn: mathFunctions[k] });
  for (const k of Object.keys(mathConstants)) host.set(k, { kind: 'const', value: mathConstants[k] });
}

// ───────────── 생성 코드가 쓰는 도우미 ─────────────
const R = { U, JDouble, JList, JConst, Early, EARLY_NULL, JStruct, copyValue, own, equalS2, truthy, forItems, repeatCount, add2, sub2, mul2, div2, mod2, equal2, less2, greater2, and2, or2, not1, length1, add3, mul3 };

function helpers(meta) {
  const E = (m, t = null) => new JyunoError(m, t);
  const noValue = (name) => { throw E(`'${name}'에서 값을 가져올수 없습니다.`); };
  const notVar = (name) => { throw E(`'${name}'은/는 변수가 아닙니다.`); };
  const noSubstitute = () => { throw E('런타임에서 변수 대입 연산을 허용하지 않습니다.'); };
  return {
    $B: builtins,
    $rv(h, name) { if (typeof h === 'function') noValue(name); return h instanceof JConst ? h.v : h; },
    $chkSet(h, name, v) { if (typeof h === 'function') notVar(name); if (h instanceof JConst) throw E('상수를 변경할수 없습니다.'); return v; },
    $localSet(h, name, v) { if (h === U) return v; if (typeof h === 'function') notVar(name); if (h instanceof JConst) throw E('상수를 변경할수 없습니다.'); return v; },
    $call(h, hasArgs, closed, argsFn) {
      if (typeof h === 'function') return h(...argsFn());
      const v = h instanceof JConst ? h.v : h instanceof HostVar ? h.get() : h;
      if (!hasArgs) return v;
      if (v instanceof JList) {
        const a = argsFn();
        switch (a.length) {
          case 0: return v;
          case 1: return listGet(v, a[0]);
          case 2: if (meta.blockSubstitute) noSubstitute(); return listSet(v, a[0], a[1]);
          default: throw E("배열/리스트는 '(이름) (인덱스)' 또는 '(이름) (인덱스) (값)' 형식으로만 쓸수 있습니다.");
        }
      }
      return closed ? new Early(v) : v;
    },
    $lget: listGet,
    $lset: listSet,
    $rc1: (a) => repeatCount([a]),
    $grammar(line, msg) { throw new GrammarSignal(line, msg); },
    $noValue: noValue,
    $unknownVar(name) { throw E(`'${name}'은/는 알수없는 변수/함수 입니다.`); },
    $unknownCall(name) { throw E(`'${name}'은/는 존재하지 않는 함수/변수/명령어 입니다.`); },
    $notVar: notVar,
    $constAssign() { throw E('상수에 값을 대입할수 없습니다.'); },
    $noSubstitute: noSubstitute,
    $cannotProcess() { throw E('처리할수 없습니다.'); },
    $parenError() { throw E("괄호 '(' 다음에는 ')'로 끝나야만 합니다."); },
    $badIndexArgs() { throw E("배열/리스트는 '(이름) (인덱스)' 또는 '(이름) (인덱스) (값)' 형식으로만 쓸수 있습니다."); },
    $forSyntax() { throw E("for는 'for (이름) (배열/리스트/문자열)' 형식으로만 쓸수 있습니다."); },
    $forArity() { throw E('for 다음에는 변수 이름과 순회할 값 하나만 와야 합니다.'); },
    $funcSyntax() { throw E('func 다음에는 함수 이름 하나만 와야 합니다.'); },
    $paramSyntax() { throw E('param 다음에는 파라미터 이름 하나만 와야 합니다.'); },
    $localSyntax() { throw E('local 다음에는 변수 이름이 와야 합니다.'); },
    $localForm() { throw E("local은 'local (이름)' 또는 'local (이름) = (값)' 형식으로만 쓸수 있습니다."); },
    $gotoMissing() { throw E('이동할 값을 넣지 않았습니다.'); },
    $gotoFail(v, line) {
      const n = tryInt(v);
      if (n === undefined) throw E('실행 위치는 음이 아닌 정수여야 합니다.');
      if (n < 0) throw E('실행 위치를 음수로 이동할수 없습니다.');
      if (n > meta.lineCount) throw E('실행 위치가 스크립트의 범위를 벗어났습니다.');
      const o = n < meta.lineCount ? ownerOf(Number(n)) : -1;
      if (o !== ownerOf(line)) throw E('goto로 함수 안팎을 넘나들수 없습니다.');
      throw E(`Native JS 제약: goto로 ${n}번 줄로 이동할수 없습니다. (같은 블록이나 바깥 블록의 레이블/줄로만 이동할수 있습니다)`, 'NativeJsRestriction');
    },
    $tooDeep() { throw E('함수 호출이 너무 깊습니다. (최대 200번)'); },
    $structSyntax() { throw E('struct 다음에는 구조체 이름 하나만 와야 합니다.'); },
    // 구조체 함수를 이름이 정해지지 않은 자리에서 부를 때: 구조체 값을 this로 묶음
    $bm(self, h) { return typeof h === 'function' ? h.bind(self) : h; },
    // 구조체 값을 변수(prefix)에 넣으면 같은 영역에 직접 만든 'prefix.path' 이름은 덮어써져 사라짐
    $unbindIf(s, path, cur) {
      for (const p of path) { if (!(s instanceof JStruct)) return cur; s = s[p]; if (s === undefined || s === U) return cur; }
      return U;
    },
    $aliasDefine(name) { throw E(`Native JS 제약: 구조체 값의 멤버 이름 '${name}'을/를 다시 정의할수 없습니다.`, 'NativeJsRestriction'); },
  };
  // 줄이 속한 함수 줄 (meta.functions: [시작 줄, endfunc 줄] 목록, 안쪽 함수가 뒤에 옴)
  function ownerOf(line) {
    let o = -1;
    for (const [start, end] of meta.functions) if (start < line && line <= end) o = start;
    return o;
  }
}

// 생성 코드가 호스트 함수/변수를 가져가는 통로 (없는 이름은 쓸때 C#과 같은 오류)
function hostApi(host) {
  const missing = (name) => { throw new JyunoError(`'${name}'은/는 존재하지 않는 함수/변수/명령어 입니다.`); };
  return {
    fn(name) { const e = host.get(name); return e && e.kind === 'func' ? e.fn : () => missing(name); },
    var(name) { const e = host.get(name); return e && e.kind === 'var' ? new HostVar(e.get, e.set) : new HostVar(() => missing(name), () => missing(name)); },
    holder(name) {
      const e = host.get(name);
      if (!e) return new HostVar(() => missing(name), () => missing(name));
      if (e.kind === 'const') return new HostVar(() => e.value, () => { throw new JyunoError('상수에 값을 대입할수 없습니다.'); });
      if (e.kind === 'func') { const f = e.fn; return (...a) => f(a); }
      return new HostVar(e.get, e.set);
    },
  };
}

// ───────────── 오류 위치: 스택 트레이스에서 생성 코드의 줄을 찾아 Jyuno 줄로 바꿈 (실행 중에는 비용 없음) ─────────────
const FRAME_RE = /([^\s()@]+):(\d+):\d+\)?\s*$/;
function frames(e) {
  const stack = e && typeof e.stack === 'string' ? e.stack : '';
  const out = [];
  for (const l of stack.split('\n')) { const m = FRAME_RE.exec(l); if (m) out.push({ file: m[1], line: +m[2] }); }
  return out;
}

function createProgram(factory, meta) {
  let base = null; // { file, offset }
  const where = () => {
    if (base) return base;
    const f = frames(meta.probe)[0];
    base = f ? { file: f.file, offset: f.line - meta.probeLine } : { file: null, offset: 0 };
    return base;
  };
  function errorLine(e) {
    const b = where();
    if (b.file === null) return -1;
    for (const f of frames(e)) {
      if (f.file !== b.file) continue;
      const l = meta.lineMap[f.line - b.offset - meta.bodyLine];
      if (l === -2) continue; // 함수 머리(호출 깊이 검사): 호출한 줄로
      if (l !== undefined && l >= 0) return l;
    }
    return -1;
  }
  /**
   * 프로그램을 실행합니다.
   * options.write(text): 출력 (기본: 모아서 output으로 돌려줌)
   * options.input: console.readline/console.read가 읽을 문자열, 또는 options.readLine()/options.read()
   * options.host: 추가 호스트 { 이름: 함수(args) | { kind: 'func', fn } | { kind: 'var', get, set } | { kind: 'const', value } }
   * 결과: { status: 'ok' | 'error' | 'grammar', value, text, type, message, line, exceptionType, output }
   */
  function run(options = {}) {
    let output = '';
    const reader = new StringReader(options.input ?? '');
    const io = {
      write: options.write ?? ((s) => { output += s; }),
      readLine: options.readLine ?? (() => reader.readLine()),
      read: options.read ?? (() => reader.read()),
      clear: options.clear ?? (() => { if (!options.write) output = ''; }),
      title: options.title ?? (() => {}),
    };
    const host = new Map();
    addConsole(host, io);
    addMath(host);
    // 프로그램이 추가한 함수에는 구조체 값의 복사본을 넘김 (C#의 StructValue와 같음)
    const ownArgs = (fn) => (args) => fn(args.map(own));
    for (const [name, e] of Object.entries(options.host ?? {})) host.set(name, typeof e === 'function' ? { kind: 'func', fn: ownArgs(e) } : e && e.kind === 'func' ? { ...e, fn: ownArgs(e.fn) } : e);
    const main = factory(R, helpers(meta), hostApi(host));
    const limit = Error.stackTraceLimit;
    Error.stackTraceLimit = 200;
    const done = (r) => (options.write ? r : { ...r, output });
    try {
      const value = main();
      return done({ status: 'ok', value, text: describe(value), type: typeName(value) });
    } catch (e) {
      if (e instanceof GrammarSignal) return done({ status: 'grammar', message: e.message, line: e.line });
      const line = errorLine(e);
      if (e && e.hostAbort) { e.jyunoLine = line; throw e; } // 출력 제한 등은 호스트가 처리 (오류 줄은 e.jyunoLine)
      if (e instanceof JyunoError) return done({ status: 'error', message: e.message, line, exceptionType: e.netType });
      if (e instanceof RangeError && /string length/i.test(e.message)) return done({ status: 'error', message: 'Insufficient memory to continue the execution of the program.', line, exceptionType: 'OutOfMemoryException' });
      if (e instanceof RangeError && /call stack/i.test(e.message)) return done({ status: 'error', message: '함수 호출이 너무 깊습니다.', line, exceptionType: 'StackOverflow' });
      return done({ status: 'error', message: String((e && e.message) || e), line, exceptionType: (e && e.name) || 'Error' });
    } finally {
      Error.stackTraceLimit = limit;
    }
  }
  // Node.js에서 직접 실행 (node 파일.js): 표준 입출력 사용
  function main() {
    const fs = require('fs');
    let pending = '';
    const flush = () => { if (pending) { fs.writeSync(1, pending); pending = ''; } };
    let buffer = Buffer.alloc(0), eof = false;
    const fill = () => {
      if (eof) return false;
      const chunk = Buffer.alloc(65536);
      for (;;) {
        try { const n = fs.readSync(0, chunk, 0, chunk.length, null); if (n === 0) { eof = true; return false; } buffer = Buffer.concat([buffer, chunk.subarray(0, n)]); return true; }
        catch (e) { if (e.code === 'EAGAIN') continue; if (e.code === 'EOF') { eof = true; return false; } throw e; }
      }
    };
    let text = '';
    const more = () => { flush(); if (!fill()) return false; const nl = buffer.lastIndexOf(10); const cut = nl < 0 ? (eof ? buffer.length : 0) : nl + 1; text += buffer.subarray(0, cut).toString('utf8'); buffer = buffer.subarray(cut); return true; };
    const reader = { readLine() { for (;;) { const i = text.search(/\r\n|\n|\r/); if (i >= 0) { const line = text.slice(0, i); text = text.slice(text[i] === '\r' && text[i + 1] === '\n' ? i + 2 : i + 1); return line; } if (!more()) { if (buffer.length) { text += buffer.toString('utf8'); buffer = Buffer.alloc(0); } if (!text) return null; const line = text; text = ''; return line; } } },
      read() { if (!text && !more() && !buffer.length) return -1; if (!text) { text = buffer.toString('utf8'); buffer = Buffer.alloc(0); } const c = text.charCodeAt(0); text = text.slice(1); return c; } };
    const r = run({
      write: (s) => { pending += s; if (pending.length > 65536) flush(); },
      readLine: () => reader.readLine(),
      read: () => reader.read(),
      clear: () => { flush(); fs.writeSync(1, '\x1b[2J\x1b[H'); },
      title: (t) => { flush(); fs.writeSync(1, `\x1b]0;${t}\x07`); },
    });
    flush();
    if (r.status === 'grammar') { process.stderr.write(`${meta.name}:${r.line + 1}: 문법 오류: ${r.message}\n`); process.exitCode = 1; }
    else if (r.status === 'error') { process.stderr.write(`${meta.name}${r.line >= 0 ? ':' + (r.line + 1) : ''}: 오류: ${r.message}\n`); process.exitCode = 1; }
    return r;
  }
  // 호스트 함수를 만들때 쓰는 런타임 (값 표현과 오류)
  const runtime = { JyunoError, JDouble, JList, JStruct, D, str, describe, typeName, tryInt, tryReal, truthy, newArray, newList, nullError };
  return { run, main, runtime, name: meta.name, lineCount: meta.lineCount };
}
