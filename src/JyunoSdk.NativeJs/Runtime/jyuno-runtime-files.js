// Jyuno Native JS 런타임의 파일, 디렉토리, 경로 명령어 (JyunoSdk 0.7.0, Jyuno 1.7.1의 path.*, file.*, directory.*)
// 변환한 프로그램이 이 명령어를 쓸 때만 jyuno-runtime.js 다음에 들어감 (쓰지 않는 프로그램은 전과 같은 코드, 같은 크기)
// jyuno-runtime.js의 값 표현과 도우미(JList, str, show, err, nullError, memReserve 등)를 씀

// ───────────── 파일, 디렉토리, 경로 (Jyuno 1.7.1의 path.*, file.*, directory.*, JyunoSdk 0.7.0) ─────────────
// C#의 System.IO(Path, File, Directory)를 옮긴 것: 경로 계산은 .NET 8의 코드를 그대로 따르고, 파일 작업은 Node.js의 fs(동기 API)로 하며
// 오류는 .NET과 같은 메시지 ('파일 작업을 하지 못했습니다: ...', Jyuno의 JyunoCommands.io)
// 리눅스/macOS는 .NET의 Unix 구현과 맞추고, 윈도우는 .NET의 Windows 구현과 비슷하게 맞춤 (오류 메시지 등은 다를수 있음)
// 변환한 코드가 이 명령어를 쓸 때만 등록함 (meta.files: Runtime.AddJyunoCommandType.File을 포함한 전역 이름으로 변환한 경우)
const PROC = typeof process === 'object' && process !== null ? process : null;
function nodeModule(name) {
  try { if (PROC && typeof PROC.getBuiltinModule === 'function') { const m = PROC.getBuiltinModule(name); if (m) return m; } } catch (e) { /* 없음 */ }
  try { if (typeof require === 'function') return require(name); } catch (e) { /* 없음 */ }
  return null;
}
// 실행 환경: options.files = { fs, platform, cwd(), chdir(path), env } (생략한 것은 Node.js의 fs, process)
function fileSystem(o) {
  const fs = o.fs !== undefined ? o.fs : nodeModule('fs');
  const platform = o.platform ?? (PROC && typeof PROC.platform === 'string' ? PROC.platform : 'linux');
  const win = platform === 'win32';
  const proc = PROC && typeof PROC.cwd === 'function' ? PROC : null;
  let here = win ? 'C:\\' : '/'; // process가 없는 환경의 현재 디렉토리
  return {
    fs, platform, win,
    caseless: win || platform === 'darwin', // PathInternal.IsCaseSensitive
    birth: platform !== 'linux' && platform !== 'android', // .NET은 리눅스에서 만든 시간 대신 상태 변경/수정 시간 중 이른 것을 씀
    env: o.env ?? (PROC && PROC.env) ?? {},
    cwd: o.cwd ?? (proc ? () => proc.cwd() : () => here),
    chdir: o.chdir ?? (proc && typeof proc.chdir === 'function' ? (p) => proc.chdir(p) : (p) => {
      if (!fs.statSync(p).isDirectory()) throw Object.assign(new Error('ENOTDIR'), { code: 'ENOTDIR' });
      here = p;
    }),
    euid: o.euid ?? (PROC && typeof PROC.geteuid === 'function' ? PROC.geteuid() : -1),
    groups: o.groups ?? (() => {
      const g = PROC && typeof PROC.getgroups === 'function' ? PROC.getgroups() : [];
      if (PROC && typeof PROC.getegid === 'function') g.push(PROC.getegid());
      return g;
    }),
  };
}
// .NET이 errno를 문자열로 바꾼 것 (strerror, glibc / macOS)
const STRERROR = {
  EPERM: 'Operation not permitted', ENOENT: 'No such file or directory', EIO: 'Input/output error', ENXIO: 'No such device or address',
  EBADF: 'Bad file descriptor', EAGAIN: 'Resource temporarily unavailable', ENOMEM: 'Cannot allocate memory', EACCES: 'Permission denied',
  EBUSY: 'Device or resource busy', EEXIST: 'File exists', EXDEV: 'Invalid cross-device link', ENODEV: 'No such device',
  ENOTDIR: 'Not a directory', EISDIR: 'Is a directory', EINVAL: 'Invalid argument', ENFILE: 'Too many open files in system',
  EMFILE: 'Too many open files', ETXTBSY: 'Text file busy', EFBIG: 'File too large', ENOSPC: 'No space left on device',
  ESPIPE: 'Illegal seek', EROFS: 'Read-only file system', EMLINK: 'Too many links', EPIPE: 'Broken pipe',
  ENAMETOOLONG: 'File name too long', ENOSYS: 'Function not implemented', ENOTEMPTY: 'Directory not empty',
  ELOOP: 'Too many levels of symbolic links', EOPNOTSUPP: 'Operation not supported', ENOTSUP: 'Operation not supported',
  EDQUOT: 'Disk quota exceeded', ESTALE: 'Stale file handle', EINTR: 'Interrupted system call', ECANCELED: 'Operation canceled',
};
const STRERROR_BSD = { EBUSY: 'Resource busy', EXDEV: 'Cross-device link', EDQUOT: 'Disc quota exceeded', ESTALE: 'Stale NFS file handle' };
const STRERROR_WIN = {
  ENOTEMPTY: 'The directory is not empty.', EINVAL: 'The parameter is incorrect.', EXDEV: 'The system cannot move the file to a different disk drive.',
  ENOSPC: 'There is not enough space on the disk.', ELOOP: 'The name of the file cannot be resolved by the system.', EBUSY: 'The process cannot access the file because it is being used by another process.',
  EROFS: 'The media is write protected.', EIO: 'The request could not be performed because of an I/O device error.',
};
const RANDOM_NAME = 'abcdefghijklmnopqrstuvwxyz012345';
const ALNUM = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
function randomBytes(n) {
  const b = new Uint8Array(n);
  const c = typeof globalThis === 'object' && globalThis.crypto;
  if (c && typeof c.getRandomValues === 'function') c.getRandomValues(b);
  else for (let i = 0; i < n; i++) b[i] = Math.floor(Math.random() * 256);
  return b;
}
// char.ToUpperInvariant (글자 하나)
const upper1 = (c) => { const u = c.toUpperCase(); return u.length === 1 ? u : c; };
const sameChar = (a, b, ic) => a === b || (ic && upper1(a) === upper1(b));

// System.IO.Path (경로 문자열 계산, 파일 시스템을 쓰지 않음)
function netPath(sys) {
  const win = sys.win;
  const SEP = win ? '\\' : '/';
  const isSep = win ? (c) => c === '\\' || c === '/' : (c) => c === '/';
  const isDrive = (c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
  const isExtended = (p) => p.length >= 4 && p[0] === '\\' && (p[1] === '\\' || p[1] === '?') && p[2] === '?' && p[3] === '\\';
  const isDevice = (p) => isExtended(p) || (p.length >= 4 && isSep(p[0]) && isSep(p[1]) && (p[2] === '.' || p[2] === '?') && isSep(p[3]));
  const isDeviceUnc = (p) => p.length >= 8 && isDevice(p) && isSep(p[7]) && p[4] === 'U' && p[5] === 'N' && p[6] === 'C';
  const effectivelyEmpty = win ? (p) => /^ *$/.test(p) : (p) => p.length === 0;
  function rootLength(p) {
    if (!win) return p.length > 0 && p[0] === '/' ? 1 : 0;
    const n = p.length;
    const device = isDevice(p), deviceUnc = device && isDeviceUnc(p);
    let i = 0;
    if ((!device || deviceUnc) && n > 0 && isSep(p[0])) {
      if (deviceUnc || (n > 1 && isSep(p[1]))) {
        i = deviceUnc ? 8 : 2;
        let k = 2;
        while (i < n && (!isSep(p[i]) || --k > 0)) i++;
      } else i = 1;
    } else if (device) {
      i = 4;
      while (i < n && !isSep(p[i])) i++;
      if (i < n && i > 4 && isSep(p[i])) i++;
    } else if (n >= 2 && p[1] === ':' && isDrive(p[0])) {
      i = 2;
      if (n > 2 && isSep(p[2])) i++;
    }
    return i;
  }
  const isRooted = win ? (p) => (p.length >= 1 && isSep(p[0])) || (p.length >= 2 && isDrive(p[0]) && p[1] === ':') : (p) => p[0] === '/';
  const isFullyQualified = win ? (p) => {
    if (p.length < 2) return false;
    if (isSep(p[0])) return p[1] === '?' || isSep(p[1]);
    return p.length >= 3 && p[1] === ':' && isSep(p[2]) && isDrive(p[0]);
  } : isRooted;
  const endsInSep = (p) => p.length > 0 && isSep(p[p.length - 1]);
  const trimEndSep = (p) => (endsInSep(p) && p.length !== rootLength(p) ? p.slice(0, -1) : p);
  // PathInternal.NormalizeDirectorySeparators: 이어진 구분자는 하나로 (윈도우는 처음 둘(UNC)을 남기고 '\'로)
  function normalizeSeps(p) {
    if (!p) return p;
    if (!win) return p.replace(/\/{2,}/g, '/');
    let out = '', start = 0;
    if (isSep(p[0])) { start = 1; out = '\\'; }
    for (let i = start; i < p.length; i++) {
      let c = p[i];
      if (isSep(c)) { if (i + 1 < p.length && isSep(p[i + 1])) continue; c = '\\'; }
      out += c;
    }
    return out;
  }
  function directoryNameEnd(p) {
    const root = rootLength(p);
    let end = p.length;
    if (end <= root) return -1;
    while (end > root && !isSep(p[--end]));
    while (end > root && isSep(p[end - 1])) end--;
    return end;
  }
  function getDirectoryName(p) {
    if (effectivelyEmpty(p)) return null;
    const end = directoryNameEnd(p);
    return end >= 0 ? normalizeSeps(p.slice(0, end)) : null;
  }
  // ReadOnlySpan 판: 구분자를 정리하지 않음
  const directoryNameSpan = (p) => { if (effectivelyEmpty(p)) return ''; const end = directoryNameEnd(p); return end >= 0 ? p.slice(0, end) : ''; };
  function getPathRoot(p) {
    if (effectivelyEmpty(p)) return null;
    if (!win) return isRooted(p) ? '/' : '';
    const n = rootLength(p);
    return n <= 0 ? '' : normalizeSeps(p.slice(0, n));
  }
  function getFileName(p) {
    const root = effectivelyEmpty(p) ? 0 : rootLength(p);
    let i = p.length - 1;
    while (i >= 0 && !isSep(p[i])) i--;
    return p.slice(i < root ? root : i + 1);
  }
  function getFileNameWithoutExtension(p) { const f = getFileName(p); const dot = f.lastIndexOf('.'); return dot < 0 ? f : f.slice(0, dot); }
  function extensionStart(p) {
    for (let i = p.length - 1; i >= 0; i--) {
      if (p[i] === '.') return i;
      if (isSep(p[i])) break;
    }
    return -1;
  }
  const getExtension = (p) => { const i = extensionStart(p); return i >= 0 && i !== p.length - 1 ? p.slice(i) : ''; };
  const hasExtension = (p) => { const i = extensionStart(p); return i >= 0 && i !== p.length - 1; };
  function changeExtension(p, ext) {
    if (p.length === 0) return '';
    const i = extensionStart(p);
    const sub = i >= 0 ? p.slice(0, i) : p;
    if (ext === null) return sub;
    return ext.startsWith('.') ? sub + ext : sub + '.' + ext;
  }
  function combine(parts) {
    let first = 0;
    for (let i = 0; i < parts.length; i++) if (parts[i].length > 0 && isRooted(parts[i])) first = i;
    let out = '';
    for (let i = first; i < parts.length; i++) {
      const s = parts[i];
      if (s.length === 0) continue;
      if (out.length === 0) out = s;
      else { if (!isSep(out[out.length - 1])) out += SEP; out += s; }
    }
    return out;
  }
  function join(parts) {
    let out = '';
    for (const s of parts) {
      if (s.length === 0) continue;
      if (out.length === 0) out = s;
      else { if (!isSep(out[out.length - 1]) && !isSep(s[0])) out += SEP; out += s; }
    }
    return out;
  }
  const joinInternal = (a, b) => (isSep(a[a.length - 1]) || isSep(b[0]) ? a + b : a + SEP + b);
  const combineInternal = (a, b) => (a.length === 0 ? b : b.length === 0 ? a : isRooted(b) ? b : joinInternal(a, b));
  // PathInternal.RemoveRelativeSegments: '//', '/./', '/../'를 없앰 (루트 아래로는 올라가지 않음)
  function removeRelativeSegments(p, rootLen) {
    let flipped = false;
    let skip = rootLen;
    if (isSep(p[skip - 1])) skip--;
    const out = skip > 0 ? p.slice(0, skip).split('') : [];
    for (let i = skip; i < p.length; i++) {
      let c = p[i];
      if (isSep(c) && i + 1 < p.length) {
        if (isSep(p[i + 1])) continue;
        if ((i + 2 === p.length || isSep(p[i + 2])) && p[i + 1] === '.') { i++; continue; }
        if (i + 2 < p.length && (i + 3 === p.length || isSep(p[i + 3])) && p[i + 1] === '.' && p[i + 2] === '.') {
          let s;
          for (s = out.length - 1; s >= skip; s--) {
            if (isSep(out[s])) { out.length = i + 3 >= p.length && s === skip ? s + 1 : s; break; }
          }
          if (s < skip) out.length = skip;
          i += 2;
          continue;
        }
      }
      if (c !== SEP && c === '/') { c = SEP; flipped = true; }
      out.push(c);
    }
    if (!flipped && out.length === p.length) return p;
    if (skip !== rootLen && out.length < rootLen) out.push(p[rootLen - 1]);
    return out.join('');
  }
  // GetFullPathName (윈도우): 현재 디렉토리/드라이브로 완성하고 '.', '..', 구성 요소 끝의 점과 공백을 정리, 짧은 이름(~)은 긴 이름으로
  function winFullPath(p) {
    if (isExtended(p)) return p;
    const s = p.replace(/\//g, '\\');
    let abs;
    if (s.length >= 2 && s[0] === '\\' && s[1] === '\\') abs = s;
    else if (s.length >= 2 && s[1] === ':' && isDrive(s[0])) {
      if (s.length >= 3 && s[2] === '\\') abs = s;
      else {
        const cwd = sys.cwd(), drive = upper1(s[0]);
        const base = cwd.length >= 2 && cwd[1] === ':' && upper1(cwd[0]) === drive ? cwd : (sys.env[`=${drive}:`] ?? `${s.slice(0, 2)}\\`);
        abs = s.length > 2 ? joinInternal(base, s.slice(2)) : base;
      }
    } else if (s[0] === '\\') {
      const cwd = sys.cwd();
      abs = cwd.slice(0, rootLength(cwd)).replace(/\\$/, '') + s;
    } else abs = joinInternal(sys.cwd(), s);
    const root = rootLength(abs);
    let r = removeRelativeSegments(abs, root);
    const head = r.slice(0, root), parts = r.slice(root).split('\\');
    for (let i = 0; i < parts.length; i++) { const t = parts[i].replace(/[. ]+$/, ''); if (t.length > 0 && parts[i] !== '.' && parts[i] !== '..') parts[i] = t; }
    r = head + parts.join('\\');
    if (r.includes('~') && sys.fs && sys.fs.realpathSync && typeof sys.fs.realpathSync.native === 'function') {
      // TryExpandShortFileName: 있는 부분까지 긴 이름으로
      const trail = endsInSep(r) && r.length > root ? '\\' : '';
      const body = trail ? r.slice(0, -1) : r;
      for (let end = body.length; end > root;) {
        try { return sys.fs.realpathSync.native(body.slice(0, end)) + body.slice(end) + trail; } catch (e) { /* 위로 */ }
        let i = end - 1;
        while (i > root && body[i] !== '\\') i--;
        end = i;
      }
    }
    return r;
  }
  // Path.GetFullPathInternal
  function fullPathCore(p) {
    if (win) return winFullPath(p);
    if (!isRooted(p)) p = combine([sys.cwd(), p]);
    const r = removeRelativeSegments(p, rootLength(p));
    return r.length === 0 ? '/' : r;
  }
  return {
    SEP, win, isSep, isRooted, isFullyQualified, isDevice, endsInSep, trimEndSep, effectivelyEmpty, rootLength,
    getDirectoryName, directoryNameSpan, getPathRoot, getFileName, getFileNameWithoutExtension, getExtension, hasExtension, changeExtension,
    combine, join, joinInternal, combineInternal, removeRelativeSegments, fullPathCore,
    // PathInternal.AreRootsEqual + GetCommonPathLength
    rootsEqual(a, b, ic) {
      const n = rootLength(a);
      if (n !== rootLength(b)) return false;
      for (let i = 0; i < n; i++) if (!sameChar(a[i], b[i], ic)) return false;
      return true;
    },
    commonPathLength(a, b, ic) {
      let n = 0;
      while (n < a.length && n < b.length && sameChar(a[n], b[n], ic)) n++;
      if (n === 0) return 0;
      if (n === a.length && (n === b.length || isSep(b[n]))) return n;
      if (n === b.length && isSep(a[n])) return n;
      while (n > 0 && !isSep(a[n - 1])) n--;
      return n;
    },
    volumeName(p) {
      const n = effectivelyEmpty(p) ? 0 : rootLength(p);
      let r = p.slice(0, n);
      if (r.length === 0) return r;
      const start = !isDevice(p) && p.startsWith('\\\\') ? 2 : isDevice(p) ? (isDeviceUnc(p) ? 8 : 4) : 0;
      r = r.slice(start);
      return endsInSep(r) ? r.slice(0, -1) : r;
    },
  };
}

// FileSystemName.TranslateWin32Expression: '*.', '?', '.?', '.*'를 DOS 와일드카드로
function translateWin32(e) {
  if (!e || e === '*' || e === '*.*') return '*';
  let out = '';
  for (let i = 0; i < e.length; i++) {
    const c = e[i];
    if (c === '.') {
      if (i >= 1 && i === e.length - 1 && e[i - 1] === '*') out = out.slice(0, -1) + '<';
      else if (i < e.length - 1 && (e[i + 1] === '?' || e[i + 1] === '*')) out += '"';
      else out += '.';
    } else if (c === '?') out += '>';
    else out += c;
  }
  return out;
}
// FileSystemName.MatchesWin32Expression (.NET의 상태 기계를 그대로 옮김)
function matchWin32(expr, name, ic) {
  if (expr.length === 0 || name.length === 0) return false;
  if (expr[0] === '*') {
    if (expr.length === 1) return true;
    const rest = expr.slice(1);
    if (!/["*<>?]/.test(rest)) {
      if (name.length < rest.length) return false;
      const tail = name.slice(name.length - rest.length);
      for (let i = 0; i < rest.length; i++) if (!sameChar(rest[i], tail[i], ic)) return false;
      return true;
    }
  }
  let nameOffset = 0, matchCount = 1, nameChar = '\0', nameFinished = false;
  let prior = [0], current = [];
  const maxState = expr.length * 2;
  while (!nameFinished) {
    if (nameOffset < name.length) nameChar = name[nameOffset++];
    else { if (prior[matchCount - 1] === maxState) break; nameFinished = true; }
    let priorMatch = 0, currentMatch = 0, priorMatchCount = 0;
    current = [];
    while (priorMatch < matchCount) {
      let off = (prior[priorMatch++] + 1) >> 1;
      outer: while (off < expr.length) {
        let state = off * 2;
        let ec = expr[off];
        let mode; // 0: 0번 이상, 1: 0번만, 2: 다음 식 글자
        if (ec === '*') mode = 0;
        else if (ec === '<') {
          let notLast = false;
          if (!nameFinished && nameChar === '.') for (let k = nameOffset; k < name.length; k++) if (name[k] === '.') { notLast = true; break; }
          mode = nameFinished || nameChar !== '.' || notLast ? 0 : 1;
        } else {
          state += 2;
          if (ec === '>') {
            if (nameFinished || nameChar === '.') mode = 2;
            else { current[currentMatch++] = state; break outer; }
          } else if (ec === '"') {
            if (nameFinished) mode = 2;
            else { if (nameChar === '.') current[currentMatch++] = state; break outer; }
          } else {
            if (ec === '\\') {
              if (++off === expr.length) { current[currentMatch++] = maxState; break outer; }
              state = off * 2 + 2;
              ec = expr[off];
            }
            if (nameFinished) break outer;
            if (ec === '?' || sameChar(ec, nameChar, ic)) current[currentMatch++] = state;
            break outer;
          }
        }
        if (mode === 0) current[currentMatch++] = state;
        if (mode <= 1) current[currentMatch++] = state + 1;
        if (++off === expr.length) current[currentMatch++] = maxState;
      }
      // 같은 상태를 다시 넣지 않음
      if (priorMatch < matchCount && priorMatchCount < currentMatch) {
        while (priorMatchCount < currentMatch) {
          while (priorMatch < matchCount && prior[priorMatch] < current[priorMatchCount]) priorMatch++;
          priorMatchCount++;
        }
      }
    }
    if (currentMatch === 0) return false;
    prior = current;
    matchCount = currentMatch;
  }
  return prior[matchCount - 1] === maxState;
}

function addFiles(host, options) {
  const sys = fileSystem(options.files || {});
  const P = netPath(sys);
  const { SEP, win, isSep, endsInSep, trimEndSep, fullPathCore } = P;
  const fs = sys.fs;
  const NL = win ? '\r\n' : '\n';
  // JyunoCommands.io: .NET의 IOException 등은 '파일 작업을 하지 못했습니다: (메시지)'
  const fail = (m) => new JyunoError(`파일 작업을 하지 못했습니다: ${m}`);
  const needFs = () => { if (!fs) throw fail('이 환경에서는 파일 시스템을 쓸수 없습니다. (run의 files.fs)'); return fs; };
  const accessDenied = (p) => (p ? `Access to the path '${p}' is denied.` : 'Access to the path is denied.');
  const pathNotFound = (p) => (p ? `Could not find a part of the path '${p}'.` : 'Could not find a part of the path.');
  const emptyArg = (param) => fail(`The value cannot be an empty string. (Parameter '${param}')`);
  // Interop.GetExceptionForIoErrno: errno(Node.js의 code)를 .NET 예외 메시지로 (path가 비었다면 경로 없는 메시지)
  function errnoMessage(code, path, isDir) {
    const p = path || null;
    switch (code) {
      case 'ENOENT': case 'ENOTDIR':
        if (code === 'ENOTDIR' || isDir) return pathNotFound(p);
        return p ? `Could not find file '${p}'.` : 'Unable to find the specified file.';
      case 'EACCES': case 'EBADF': case 'EPERM': return accessDenied(p);
      case 'ENAMETOOLONG': return p ? `The path '${p}' is too long, or a component of the specified path is too long.` : 'The specified file name or path is too long, or a component of the specified path is too long.';
      case 'EWOULDBLOCK': return p ? `The process cannot access the file '${p}' because it is being used by another process.` : 'The process cannot access the file because it is being used by another process.';
      case 'EEXIST':
        if (p) return win && isDir ? `Cannot create '${p}' because a file or directory with the same name already exists.` : `The file '${p}' already exists.`;
        break;
    }
    const m = (win ? STRERROR_WIN[code] : sys.platform === 'darwin' ? STRERROR_BSD[code] : undefined) ?? STRERROR[code] ?? code;
    return p ? `${m} : '${p}'` : m;
  }
  // Node.js의 파일 시스템 오류만 바꾸고, 다른 예외(메모리 한도 등)는 그대로
  const isSysError = (e) => e && typeof e.code === 'string' && /^E[A-Z]+$/.test(e.code);
  const fsFail = (e, path, isDir = false) => (isSysError(e) ? fail(errnoMessage(e.code, path, isDir)) : e);
  const tryStat = (p) => { try { return fs.statSync(p); } catch (e) { return null; } };
  const tryLstat = (p) => { try { return fs.lstatSync(p); } catch (e) { return null; } };
  const sameFile = (a, b) => a.dev === b.dev && a.ino === b.ino;
  const big = (p, link) => (link ? fs.lstatSync(p, { bigint: true }) : fs.statSync(p, { bigint: true }));
  // string.Equals(a, b, 윈도우/macOS는 OrdinalIgnoreCase)
  const eqPath = (a, b) => { if (a.length !== b.length) return false; for (let i = 0; i < a.length; i++) if (!sameChar(a[i], b[i], sys.caseless)) return false; return true; };
  const startsWithPath = (a, prefix) => a.length >= prefix.length && eqPath(a.slice(0, prefix.length), prefix);

  // ── Path ──
  // Path.GetFullPath: 빈 문자열, 널 문자는 ArgumentException
  function getFullPath(p) {
    if (win ? P.effectivelyEmpty(p) : p.length === 0) throw win ? fail("The path is empty. (Parameter 'path')") : emptyArg('path');
    if (p.includes('\0')) throw fail("Null character in path. (Parameter 'path')");
    return fullPathCore(p);
  }
  function getFullPathFrom(p, base) {
    if (!P.isFullyQualified(base)) throw fail("Basepath argument is not fully qualified. (Parameter 'basePath')");
    if (base.includes('\0') || p.includes('\0')) throw fail('Null character in path.');
    if (P.isFullyQualified(p)) return fullPathCore(p);
    if (!win) return fullPathCore(P.combineInternal(base, p));
    if (P.effectivelyEmpty(p)) return base;
    let combined;
    if (isSep(p[0])) combined = P.join([base.slice(0, P.rootLength(base)), p.slice(1)]);
    else if (p.length >= 2 && p[1] === ':' && /[A-Za-z]/.test(p[0])) {
      if (P.volumeName(p) === P.volumeName(base)) combined = P.join([base, p.slice(2)]);
      else combined = P.isDevice(base) ? base.slice(0, 4) + p.slice(0, 2) + '\\' + p.slice(2) : p.slice(0, 2) + '\\' + p.slice(2);
    } else combined = P.joinInternal(base, p);
    return P.isDevice(combined) ? P.removeRelativeSegments(combined, P.rootLength(combined)) : fullPathCore(combined);
  }
  function cwd() { try { return sys.cwd(); } catch (e) { throw fsFail(e, null, true); } }
  function getRelativePath(from, to) {
    if (P.effectivelyEmpty(from)) throw fail("The path is empty. (Parameter 'relativeTo')");
    if (P.effectivelyEmpty(to)) throw fail("The path is empty. (Parameter 'path')");
    from = getFullPath(from);
    to = getFullPath(to);
    const ic = sys.caseless;
    if (!P.rootsEqual(from, to, ic)) return to;
    let common = P.commonPathLength(from, to, ic);
    if (common === 0) return to;
    let fromLen = from.length;
    if (endsInSep(from)) fromLen--;
    const toEnds = endsInSep(to);
    let toLen = to.length;
    if (toEnds) toLen--;
    if (fromLen === toLen && common >= fromLen) return '.';
    let out = '';
    if (common < fromLen) {
      out = '..';
      for (let i = common + 1; i < fromLen; i++) if (isSep(from[i])) out += SEP + '..';
    } else if (isSep(to[common])) common++;
    let diff = toLen - common;
    if (toEnds) diff++;
    if (diff > 0) { if (out.length > 0) out += SEP; out += to.substr(common, diff); }
    return out;
  }
  function getTempPath() {
    if (win) {
      const t = getFullPath(sys.env.TMP || sys.env.TEMP || sys.env.USERPROFILE || sys.env.SystemRoot || 'C:\\Windows');
      return endsInSep(t) ? t : t + '\\';
    }
    const t = sys.env.TMPDIR;
    return !t ? '/tmp/' : isSep(t[t.length - 1]) ? t : t + '/';
  }
  function getTempFileName() {
    const dir = getTempPath();
    needFs();
    for (let tries = 0; ; tries++) {
      const r = randomBytes(6);
      const name = win ? `tmp${((r[0] << 8) | r[1]).toString(16).toUpperCase()}.tmp` : `tmp${[...r].map((b) => ALNUM[b % 62]).join('')}.tmp`;
      const path = dir + name;
      try { fs.closeSync(fs.openSync(path, 'wx', 0o600)); return path; } catch (e) {
        if (isSysError(e) && e.code === 'EEXIST' && tries < 100) continue;
        throw fsFail(e, dir, true);
      }
    }
  }
  function getRandomFileName() {
    const r = randomBytes(11);
    let s = '';
    for (let i = 0; i < 11; i++) { if (i === 8) s += '.'; s += RANDOM_NAME[r[i] & 31]; }
    return s;
  }

  // ── 있는지 확인 (File.Exists, Directory.Exists: 예외 없이 false) ──
  function fullOrNull(p) {
    if (!fs || p.length === 0 || p.includes('\0')) return null;
    try { return fullPathCore(p); } catch (e) { return null; }
  }
  // FileSystem.FileExists: 링크라면 대상, 깨진 링크도 있는 것, 디렉토리가 아니면 파일
  function fileExistsFull(full) { const t = trimEndSep(full); const st = tryStat(t) ?? tryLstat(t); return st !== null && !st.isDirectory(); }
  const dirExistsFull = (full) => { if (!full) return false; const st = tryStat(full); return st !== null && st.isDirectory(); };
  function fileExists(p) { const full = fullOrNull(p); return full !== null && !endsInSep(full) && fileExistsFull(full); }
  function dirExists(p) { const full = fullOrNull(p); return full !== null && dirExistsFull(full); }

  // ── 파일 읽고 쓰기 ──
  // SafeFileHandle.Open: 없는 경로는 새로 만들려던 경우나 부모 디렉토리가 없을 때 디렉토리 오류, 디렉토리를 열면 접근 거부
  function openFail(e, full, creating) {
    if (!isSysError(e)) return e;
    if (e.code === 'EISDIR') return fail(accessDenied(full));
    const isDir = e.code === 'ENOENT' && (creating || !dirExistsFull(P.getDirectoryName(trimEndSep(full))));
    return fail(errnoMessage(e.code, full, isDir));
  }
  // File.ReadAll*, WriteAll*, AppendAll*, FileStream: 빈 경로는 ArgumentException
  function argPath(p, param = 'path') {
    if (p.length === 0) throw emptyArg(param);
    return getFullPath(p);
  }
  function readAll(full) { needFs(); try { return fs.readFileSync(full); } catch (e) { throw openFail(e, full, false); } }
  function writeAll(p, data, append) {
    const full = argPath(p);
    needFs();
    try { if (append) fs.appendFileSync(full, data); else fs.writeFileSync(full, data); } catch (e) { throw openFail(e, full, true); }
  }
  // FileInfo.Length: 링크 자체의 크기 (lstat), 디렉토리나 없는 파일은 FileNotFoundException
  function fileLength(full) {
    const t = trimEndSep(full);
    const st = tryLstat(t);
    if (st === null || st.isDirectory() || (st.isSymbolicLink() && (tryStat(t)?.isDirectory() ?? false))) throw fail(`Could not find file '${full}'.`);
    return st.size;
  }
  const DECODERS = {};
  const decoder = (label) => (DECODERS[label] ??= new TextDecoder(label, { ignoreBOM: true }));
  function utf32(b, le) {
    const cps = [];
    const n = b.length - (b.length % 4);
    for (let i = 0; i < n; i += 4) {
      const c = le ? (b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24)) >>> 0 : ((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]) >>> 0;
      cps.push(c > 0x10ffff || (c >= 0xd800 && c <= 0xdfff) ? 0xfffd : c);
    }
    if (n < b.length) cps.push(0xfffd);
    let s = '';
    for (let i = 0; i < cps.length; i += 4096) s += String.fromCodePoint(...cps.slice(i, i + 4096));
    return s;
  }
  // StreamReader(detectEncodingFromByteOrderMarks): BOM으로 UTF-8/16/32를 고르고, 없다면 UTF-8 (잘못된 바이트는 U+FFFD)
  function decodeText(b) {
    const n = b.length;
    if (n >= 2 && b[0] === 0xfe && b[1] === 0xff) {
      const s = b.slice(2);
      for (let i = 0; i + 1 < s.length; i += 2) { const t = s[i]; s[i] = s[i + 1]; s[i + 1] = t; }
      return decoder('utf-16le').decode(s);
    }
    if (n >= 2 && b[0] === 0xff && b[1] === 0xfe) {
      if (n >= 4 && b[2] === 0 && b[3] === 0) return utf32(b.subarray(4), true);
      return decoder('utf-16le').decode(b.subarray(2));
    }
    if (n >= 3 && b[0] === 0xef && b[1] === 0xbb && b[2] === 0xbf) return decoder('utf-8').decode(b.subarray(3));
    if (n >= 4 && b[0] === 0 && b[1] === 0 && b[2] === 0xfe && b[3] === 0xff) return utf32(b.subarray(4), false);
    return decoder('utf-8').decode(b);
  }
  // StreamReader.ReadLine: \r\n, \n, \r로 나누고 마지막 줄바꿈 뒤의 빈 줄은 없음
  function splitLines(s) {
    const lines = s.split(/\r\n|\r|\n/);
    if (lines[lines.length - 1] === '') lines.pop();
    return lines;
  }
  // Interpreter.ReserveString
  function reserveString(length) {
    if (length > STRING_MAX) throw err(`문자열이 너무 깁니다. (${group(String(length))}자, 최대 536,870,912자)`);
    memReserve(length * 2 + 24);
  }
  const msOf = (ns) => Number(ns >= 0n ? ns / 1000000n : -((-ns + 999999n) / 1000000n));
  // 밀리초 값이 바뀌지 않도록 그 밀리초의 가운데로 (초 단위 실수로 넘기면 반올림 오차가 생김)
  const msMid = (ms) => (ms + 0.5) / 1000;
  // FileStatus: 링크 자체의 시간 (lstat), 만든 시간이 없다면 상태 변경/수정 시간 중 이른 것
  function statTime(full, which) {
    let st;
    try { st = big(trimEndSep(full), true); } catch (e) { throw fsFail(e, full); }
    if (which === 'm') return msOf(st.mtimeNs);
    if (which === 'a') return msOf(st.atimeNs);
    if (sys.birth && st.birthtimeNs > 0n) return msOf(st.birthtimeNs);
    return msOf(st.mtimeNs < st.ctimeNs ? st.mtimeNs : st.ctimeNs);
  }
  // FileStatus의 권한: 링크라면 가리키는 대상의 권한 (소유자는 링크)
  function modeOf(full) {
    const t = trimEndSep(full);
    let st;
    try { st = fs.lstatSync(t); } catch (e) { throw fsFail(e, full); }
    if (!st.isSymbolicLink()) return { mode: st.mode, uid: st.uid, gid: st.gid };
    const target = tryStat(t);
    return { mode: target ? target.mode : st.mode, uid: st.uid, gid: st.gid };
  }
  // FileStatus.IsModeReadOnlyCore: 읽을수 있지만 쓸수 없음 (소유자/그룹/기타 중 지금 사용자에게 해당하는 것)
  function isReadOnly(full) {
    const { mode, uid, gid } = modeOf(full);
    if (win) return (mode & 0o200) === 0;
    const u = (mode & 0o400) !== 0 && (mode & 0o200) === 0;
    const g = (mode & 0o040) !== 0 && (mode & 0o020) === 0;
    const o = (mode & 0o004) !== 0 && (mode & 0o002) === 0;
    if (u === g && g === o) return u;
    if (uid === sys.euid) return u;
    if (o === g) return o;
    return sys.groups().includes(gid) ? g : o;
  }
  function setReadOnly(full, readOnly) {
    const { mode } = modeOf(full);
    const old = mode & 0o7777;
    let mode2 = old;
    if (readOnly) mode2 &= ~0o222;
    else if (mode2 & 0o400) mode2 |= 0o200;
    if (mode2 !== old) { try { fs.chmodSync(full, mode2); } catch (e) { throw fsFail(e, full); } }
  }
  // FileSystem.DeleteFile: 없는 파일은 무시, 디렉토리는 접근 거부
  function deleteFile(full) {
    try { fs.unlinkSync(full); } catch (e) {
      if (!isSysError(e) || e.code === 'ENOENT') { if (isSysError(e)) return; throw e; }
      if (e.code === 'EISDIR') throw fail(accessDenied(full));
      throw fsFail(e, full);
    }
  }
  // FileSystem.CopyFile: 대상이 디렉토리라면 오류, 같은 파일에 덮어쓰기는 공유 위반, 수정/접근 시간도 복사
  function copyFull(s, d, overwrite) {
    needFs();
    let st;
    try { st = big(s, false); } catch (e) { throw openFail(e, s, false); }
    if (st.isDirectory()) throw fail(accessDenied(s));
    const ds = (() => { try { return big(d, false); } catch (e) { return null; } })();
    if (ds !== null) {
      if (!overwrite) throw fail(ds.isDirectory() ? `The target file '${d}' is a directory, not a file.` : `The file '${d}' already exists.`);
      if (ds.isDirectory()) throw fail(accessDenied(d));
      if (sameFile(st, ds)) throw fail(errnoMessage('EWOULDBLOCK', d));
    }
    try { fs.copyFileSync(s, d, overwrite ? 0 : fs.constants.COPYFILE_EXCL); } catch (e) { throw openFail(e, d, true); }
    try { fs.utimesSync(d, msMid(msOf(st.atimeNs)), msMid(msOf(st.mtimeNs))); } catch (e) { /* 시간은 복사하지 못해도 됨 */ }
  }
  function copyFile(src, dst, overwrite) {
    if (src.length === 0) throw emptyArg('sourceFileName');
    if (dst.length === 0) throw emptyArg('destFileName');
    copyFull(getFullPath(src), getFullPath(dst), overwrite);
  }
  const LINK_FALLBACK = new Set(['EXDEV', 'EACCES', 'EPERM', 'EOPNOTSUPP', 'ENOTSUP', 'EMLINK', 'ENOSYS']);
  // FileSystem.LinkOrCopyFile: 하드 링크를 만들고, 링크를 만들수 없는 파일 시스템이라면 복사
  function linkOrCopy(s, d) {
    try { fs.linkSync(s, d); } catch (e) {
      if (!isSysError(e)) throw e;
      if (LINK_FALLBACK.has(e.code)) { copyFull(s, d, false); return; }
      if (e.code === 'ENOENT') {
        if (!dirExistsFull(P.getDirectoryName(d))) throw fail(errnoMessage('ENOENT', d, true));
        throw fail(errnoMessage('ENOENT', s, false));
      }
      throw fsFail(e, e.code === 'EEXIST' ? d : null);
    }
  }
  function moveFile(src, dst, overwrite) {
    if (src.length === 0) throw emptyArg('sourceFileName');
    if (dst.length === 0) throw emptyArg('destFileName');
    const s = getFullPath(src), d = getFullPath(dst);
    needFs();
    if (!fileExistsFull(s)) throw fail(`Could not find file '${s}'.`);
    if (overwrite) {
      try { fs.renameSync(s, d); } catch (e) {
        if (!isSysError(e)) throw e;
        if (e.code === 'EXDEV') { copyFull(s, d, true); deleteFile(s); return; }
        throw fail(errnoMessage(e.code, d, e.code === 'ENOENT' && !dirExistsFull(P.getDirectoryName(d))));
      }
      return;
    }
    try { fs.linkSync(s, d); } catch (e) {
      // 같은 파일 (같은 경로, 대소문자만 다른 이름): 이름만 바꿈
      if (isSysError(e) && e.code === 'EEXIST') {
        const a = tryLstat(s), b = tryLstat(d);
        if (a && b && sameFile(a, b)) { try { fs.renameSync(s, d); } catch (e2) { throw fsFail(e2, null); } return; }
      }
      if (isSysError(e) && LINK_FALLBACK.has(e.code)) copyFull(s, d, false);
      else linkOrCopyFail(e, s, d);
    }
    deleteFile(s);
  }
  function linkOrCopyFail(e, s, d) {
    if (!isSysError(e)) throw e;
    if (e.code === 'ENOENT') {
      if (!dirExistsFull(P.getDirectoryName(d))) throw fail(errnoMessage('ENOENT', d, true));
      throw fail(errnoMessage('ENOENT', s, false));
    }
    throw fsFail(e, e.code === 'EEXIST' ? d : null);
  }
  // FileSystem.ReplaceFile (Unix): 원본을 대상 이름으로 바꾸고, 백업이 있다면 대상을 백업 이름으로 남김
  function replaceFile(src, dst, bak) {
    const s = getFullPath(src), d = getFullPath(dst), b = bak === null ? null : getFullPath(bak);
    needFs();
    let ss;
    try { ss = big(s, true); } catch (e) { throw fsFail(e, s); }
    if (ss.isDirectory()) throw fail(`The specified path '${s}' is not a file.`);
    let ds = null;
    try { ds = big(d, true); } catch (e) { /* 없음 */ }
    if (ds !== null) {
      if (ds.isDirectory()) throw fail(`The specified path '${d}' is not a file.`);
      if (sameFile(ss, ds)) throw fail(`The source '${s}' and destination '${d}' are the same file.`);
    }
    if (b !== null) {
      try { fs.unlinkSync(b); } catch (e) { if (!isSysError(e)) throw e; if (e.code !== 'ENOENT') throw fsFail(e, b); }
      try { fs.linkSync(d, b); } catch (e) {
        if (isSysError(e) && LINK_FALLBACK.has(e.code)) copyFull(d, b, false);
        else linkOrCopyFail(e, d, b);
      }
    } else {
      try { fs.statSync(d); } catch (e) { if (isSysError(e) && e.code === 'ENOENT') throw fail(errnoMessage('ENOENT', null, false)); }
    }
    try { fs.renameSync(s, d); } catch (e) { throw fsFail(e, null); }
  }

  // ── 디렉토리 ──
  // FileSystem.CreateDirectory (Unix): 없는 부모부터 차례로 만듦
  function createDirectory(full) {
    needFs();
    if (full.length === P.rootLength(full)) return;
    const mk = (p) => { try { fs.mkdirSync(p); return null; } catch (e) { if (!isSysError(e)) throw e; return e; } };
    const e = mk(full);
    if (e === null) return;
    if (e.code === 'EEXIST' && dirExistsFull(full)) return;
    if (e.code !== 'ENOENT') throw fsFail(e, full, true);
    const stack = [full.length];
    const root = P.rootLength(full);
    let i = full.length - 1;
    if (isSep(full[i])) i--;
    do {
      while (i > 0 && !isSep(full[i])) i--;
      if (i < root) break;
      const parent = full.slice(0, i);
      const pe = mk(parent);
      if (pe === null) break;
      if (pe.code === 'ENOENT') stack.push(i);
      else if (pe.code === 'EEXIST') break;
      else throw fsFail(pe, parent, true);
      i--;
    } while (i > 0);
    for (let k = stack.length - 1; k >= 0; k--) {
      const p = full.slice(0, stack[k]);
      const ce = mk(p);
      if (ce !== null) {
        if (ce.code === 'EEXIST') { if (k !== 0) continue; if (dirExistsFull(p)) return; }
        throw fsFail(ce, p, true);
      }
    }
  }
  // 디렉토리의 항목을 readdir 순서 그대로 (readdirSync는 이름순으로 정렬하므로 opendir), 열지 못하면 .NET의 디렉토리 오류
  function eachEntry(dir, fn) {
    let h;
    try { h = fs.opendirSync(dir); } catch (e) { throw fsFail(e, dir, true); }
    try { for (let d; (d = h.readSync()) !== null;) if (fn(d) === false) return; } catch (e) { throw fsFail(e, dir, true); } finally { h.closeSync(); }
  }
  // FileSystem.RemoveDirectory (Unix)
  function removeEmpty(full, top, throwNotEmpty = true) {
    try { fs.rmdirSync(full); return true; } catch (e) {
      if (!isSysError(e)) throw e;
      const c = e.code;
      if (c === 'ENOTEMPTY' || c === 'EEXIST') { if (!throwNotEmpty) return false; throw fail(errnoMessage('ENOTEMPTY', full, true)); }
      if (c === 'ENOENT') { if (!top) return true; throw fail(pathNotFound(full)); }
      // 디렉토리를 가리키는 링크라면 링크만 지움
      if (c === 'ENOTDIR' && tryLstat(trimEndSep(full))?.isSymbolicLink()) { deleteFile(trimEndSep(full)); return true; }
      if (dirExistsFull(full)) { if (c === 'EACCES' || c === 'EPERM' || c === 'EROFS' || c === 'EISDIR') throw fail(accessDenied(full)); }
      else if (tryLstat(full) === null) throw fail(pathNotFound(full));
      throw fsFail(e, full, true);
    }
  }
  function removeRecursive(full) {
    let first = null;
    try {
      const children = [];
      eachEntry(full, (d) => { children.push([P.join([full, d.name]), d.isDirectory()]); });
      for (const [child, isDir] of children) {
        try { if (isDir) removeRecursive(child); else deleteFile(child); } catch (e) { first ??= e; }
      }
    } catch (e) { first ??= e; }
    if (first !== null) throw first;
    removeEmpty(full, false);
  }
  function removeDirectory(full, recursive) {
    needFs();
    if (!removeEmpty(full, true, !recursive)) removeRecursive(full);
  }
  // Directory.Move (Unix)
  function moveDirectory(src, dst) {
    if (src.length === 0) throw emptyArg('sourceDirName');
    if (dst.length === 0) throw emptyArg('destDirName');
    const s = getFullPath(src), d = getFullPath(dst);
    needFs();
    const sn = trimEndSep(s), dn = trimEndSep(d);
    let caseRename = false;
    if (eqPath(sn, dn)) {
      if (!sys.caseless || P.getFileName(sn) === P.getFileName(dn)) throw fail('Source and destination path must be different.');
      caseRename = true;
    }
    if (sn.length !== s.length ? !dirExistsFull(sn) : tryLstat(sn) === null) throw fail(pathNotFound(s));
    if (!caseRename && tryLstat(dn) !== null) throw fail(`Cannot create '${d}' because a file or directory with the same name already exists.`);
    try { fs.renameSync(s, d); } catch (e) {
      if (!isSysError(e)) throw e;
      if (e.code === 'EACCES') throw fail(accessDenied(s));
      if (e.code === 'ENOENT') throw fail(pathNotFound(s));
      throw fail(errnoMessage(e.code, null, true));
    }
  }
  // Directory.Enumerate*: 패턴(Win32 와일드카드), 하위 디렉토리는 넓이 우선 (디렉토리를 가리키는 링크도 따라감)
  // 결과는 지정한 경로에 항목 이름을 붙인 것 (FileSystemEntry.ToSpecifiedFullPath)
  function enumerate(directory, expression, kind, recursive) {
    if (P.isRooted(expression)) throw fail("Second path fragment must not be a drive or UNC name. (Parameter 'expression')");
    if (expression.includes('\0')) throw fail(`Null character in path. (Parameter '${expression}')`);
    if (directory.includes('\0')) throw fail(`Null character in path. (Parameter '${directory}')`);
    const dn = P.directoryNameSpan(expression);
    if (dn.length !== 0) { directory = P.join([directory, dn]); expression = expression.slice(dn.length + 1); }
    if (expression === '' || expression === '.' || expression === '*.*') expression = '*';
    else if (expression !== '*') {
      if (!win && /[\\"<>]/.test(expression)) expression = expression.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/>/g, '\\>').replace(/</g, '\\<');
      expression = translateWin32(expression);
    }
    const root = trimEndSep(getFullPath(directory));
    needFs();
    const origEnds = endsInSep(directory);
    const out = [];
    let chars = 0;
    const queue = [root];
    for (let q = 0; q < queue.length; q++) {
      const dir = queue[q];
      let rel = dir.slice(root.length);
      if (origEnds && rel.length > 0 && isSep(rel[0])) rel = rel.slice(1);
      let h;
      try { h = fs.opendirSync(dir); } catch (e) {
        if (q > 0 && isSysError(e) && (e.code === 'ENOENT' || e.code === 'ENOTDIR')) continue; // 하위 디렉토리가 없어졌다면 건너뜀
        throw fsFail(e, dir, true);
      }
      try {
        for (let d; (d = h.readSync()) !== null;) {
          const name = d.name;
          let isDir = d.isDirectory();
          if (d.isSymbolicLink()) { const st = tryStat(P.join([dir, name])); isDir = st !== null && st.isDirectory(); }
          if (isDir && recursive) queue.push(P.join([dir, name]));
          if ((kind === 'e' || (kind === 'd') === isDir) && matchWin32(expression, name, sys.caseless)) {
            const entry = P.join([directory, rel, name]);
            out.push(entry);
            chars += entry.length;
            if ((out.length & 0xfff) === 0) memReserve(chars * 2 + out.length * 40);
          }
        }
      } catch (e) { throw fsFail(e, dir, true); } finally { h.closeSync(); }
    }
    return { out, chars };
  }
  // DirectoryInfo.LinkTarget: 링크가 아니면 null
  function linkTarget(full) {
    try { return fs.readlinkSync(full); } catch (e) { if (isSysError(e) && e.code === 'EINVAL') return null; throw fsFail(e, full, true); }
  }
  // Files.copy_directory (Jyuno 1.7.1): 디렉토리를 가리키는 링크는 따라가지 않고 같은 링크를 만듦
  function copyDirectory(source, target, overwrite, name) {
    if (!dirExists(source)) throw fail(`디렉토리를 찾을수 없습니다: '${source}'`);
    const fullSource = trimEndSep(getFullPath(source));
    const fullTarget = trimEndSep(getFullPath(target));
    const prefix = endsInSep(fullSource) ? fullSource : fullSource + SEP;
    if (eqPath(fullSource, fullTarget) || startsWithPath(fullTarget, prefix)) throw err(`${name}: 대상 디렉토리가 원본 디렉토리 안에 있습니다.`);
    const pending = [[fullSource, fullTarget]];
    while (pending.length > 0) {
      const [cs, ct] = pending.pop();
      createDirectory(getFullPath(ct));
      for (const file of enumerate(cs, '*', 'f', false).out) copyFile(file, P.combine([ct, P.getFileName(file)]), overwrite);
      for (const sub of enumerate(cs, '*', 'd', false).out) {
        const dest = P.combine([ct, P.getFileName(sub)]);
        const link = linkTarget(sub);
        if (link !== null) {
          const full = getFullPath(dest);
          try { fs.symlinkSync(link, full, 'dir'); } catch (e) { throw fsFail(e, full, true); }
          continue;
        }
        pending.push([sub, dest]);
      }
    }
  }

  // ── 인자 (Files.need, text, lines_arg, bytes_arg) ──
  const need = (a, n, name) => { if (a.length < n) throw err(`${name}: 인자가 ${n}개 이상 필요합니다.`); };
  function text(a, i, name) {
    const v = a[i];
    if (typeof v === 'string') return v;
    if (v === null) throw nullError();
    throw err(`${name}: '${show(v)}'은/는 문자열이 아닙니다.`);
  }
  const lines = (a) => (a.length === 2 && a[1] instanceof JList ? a[1].a : a.slice(1)).map(str);
  function bytes(a, name) {
    const src = a.length === 2 && a[1] instanceof JList ? a[1].a : a.slice(1);
    const out = new Uint8Array(src.length);
    for (let i = 0; i < src.length; i++) {
      const v = src[i];
      if (v === null) throw nullError();
      const n = tryInt(v);
      if (n === undefined) throw err(`${name}: 바이트 값은 정수여야 합니다. ('${show(v)}')`);
      if (n < 0 || n > 255) throw err(`${name}: 바이트 값 ${n}은/는 0 ~ 255 범위를 벗어났습니다.`);
      out[i] = Number(n);
    }
    return out;
  }
  // 예전부터 있던 명령어(exist, read, write, remove, move)의 인자 검사: 경로 하나, 메시지는 그대로
  function legacyPath(a, missing, notText = missing) {
    if (a.length === 0) throw err(missing);
    if (a[0] === null) throw nullError();
    if (typeof a[0] !== 'string') throw err(notText);
    return a[0];
  }
  const strings = (list) => memHold(new JList('string', true, list.out), list.chars * 2 + list.out.length * 40);

  const add = (prefix, table) => { for (const k of Object.keys(table)) { const name = prefix + k, f = table[k]; host.set(name, { kind: 'func', fn: (a) => f(a, name) }); } };
  host.set('path.sep', { kind: 'const', value: SEP });
  host.set('path.altsep', { kind: 'const', value: '/' });
  host.set('path.pathsep', { kind: 'const', value: win ? ';' : ':' });
  host.set('path.volsep', { kind: 'const', value: win ? ':' : '/' });
  const one = (f) => (a, name) => { need(a, 1, name); return f(text(a, 0, name)); };
  add('path.', {
    combine: (a, name) => { need(a, 1, name); return P.combine(a.map((_, i) => text(a, i, name))); },
    join: (a, name) => { need(a, 1, name); return P.join(a.map((_, i) => text(a, i, name))); },
    filename: one(P.getFileName),
    filenamewithoutext: one(P.getFileNameWithoutExtension),
    extension: one(P.getExtension),
    directory: one(P.getDirectoryName),
    root: one(P.getPathRoot),
    full: (a, name) => {
      need(a, 1, name);
      const p = text(a, 0, name);
      if (a.length > 1) return getFullPathFrom(p, text(a, 1, name));
      return getFullPath(p);
    },
    relative: (a, name) => {
      need(a, 1, name);
      if (a.length === 1) { const t = text(a, 0, name); return getRelativePath(cwd(), t); }
      const from = text(a, 0, name), to = text(a, 1, name);
      return getRelativePath(from, to);
    },
    changeext: (a, name) => { need(a, 2, name); const p = text(a, 0, name); return P.changeExtension(p, a[1] === null ? null : text(a, 1, name)); },
    hasext: one(P.hasExtension),
    isrooted: one(P.isRooted),
    isfullyqualified: one(P.isFullyQualified),
    endsinsep: one(P.endsInSep),
    trimsep: one(trimEndSep),
    temp: () => getTempPath(),
    tempfile: () => getTempFileName(),
    randomname: () => getRandomFileName(),
  });
  const fileTime = (which) => one((p) => { if (!fileExists(p)) throw fail(`파일을 찾을수 없습니다: '${p}'`); return statTime(fullPathCore(p), which); });
  const dirTime = (which) => one((p) => { if (!dirExists(p)) throw fail(`디렉토리를 찾을수 없습니다: '${p}'`); return statTime(fullPathCore(p), which); });
  add('file.', {
    exist: (a) => fileExists(legacyPath(a, '파일 경로를 입력해주세요.')),
    read: (a) => {
      const p = legacyPath(a, '파일의 경로를 입력해야 합니다.');
      if (!fileExists(p)) return null;
      const full = fullPathCore(p);
      reserveString(fileLength(full));
      const s = decodeText(readAll(full));
      return memHold(s, s.length * 2);
    },
    write: (a) => {
      if (a.length < 2) throw err('파일의 경로, 그리고 내용을 입력해야합니다.');
      const p = legacyPath(a, '', '파일의 경로는 문자열이어야 합니다.');
      if (a[1] === null) throw nullError();
      writeAll(p, str(a[1]), false);
      return null;
    },
    append: (a, name) => {
      need(a, 2, name);
      const p = text(a, 0, name);
      if (a[1] === null) throw nullError();
      writeAll(p, str(a[1]), true);
      return null;
    },
    remove: (a) => {
      const p = legacyPath(a, '제거할 파일의 경로를 입력해야 합니다.', '제거할 파일의 경로는 문자열이어야 합니다.');
      if (!fileExists(p)) return false;
      deleteFile(fullPathCore(p));
      return true;
    },
    readlines: one((p) => {
      if (!fileExists(p)) return null;
      const full = fullPathCore(p);
      const length = fileLength(full);
      memReserve(length * 2 + 64);
      const result = splitLines(decodeText(readAll(full)));
      return memHold(new JList('string', true, result), length * 2 + result.length * 32);
    }),
    writelines: (a, name) => { need(a, 2, name); const p = text(a, 0, name); writeAll(p, lines(a).map((l) => l + NL).join(''), false); return null; },
    appendlines: (a, name) => { need(a, 2, name); const p = text(a, 0, name); writeAll(p, lines(a).map((l) => l + NL).join(''), true); return null; },
    readbytes: (a, name) => {
      need(a, 1, name);
      const p = text(a, 0, name);
      if (!fileExists(p)) return null;
      const full = fullPathCore(p);
      const length = fileLength(full);
      if (length > 0x7fffffc7) throw err(`${name}: 파일이 너무 큽니다. (${length}바이트)`);
      memReserve(length * 8 + 32);
      const raw = readAll(full);
      const result = new Array(raw.length);
      for (let i = 0; i < raw.length; i++) result[i] = raw[i];
      return memHold(new JList('long', true, result), result.length * 8);
    },
    writebytes: (a, name) => { need(a, 2, name); const p = text(a, 0, name); writeAll(p, bytes(a, name), false); return null; },
    appendbytes: (a, name) => { need(a, 2, name); const p = text(a, 0, name); writeAll(p, bytes(a, name), true); return null; },
    copy: (a, name) => { need(a, 2, name); const s = text(a, 0, name), d = text(a, 1, name); copyFile(s, d, a.length > 2 && truthy(a[2])); return true; },
    move: (a, name) => { need(a, 2, name); const s = text(a, 0, name), d = text(a, 1, name); moveFile(s, d, a.length > 2 && truthy(a[2])); return true; },
    replace: (a, name) => {
      need(a, 2, name);
      const s = text(a, 0, name), d = text(a, 1, name);
      const b = a.length > 2 && a[2] !== null ? text(a, 2, name) : null;
      replaceFile(s, d, b);
      return true;
    },
    size: one((p) => { const full = getFullPath(p); needFs(); return fileLength(full); }),
    touch: one((p) => {
      if (!fileExists(p)) {
        const full = argPath(p);
        needFs();
        try { fs.closeSync(fs.openSync(full, 'w')); } catch (e) { throw openFail(e, full, true); }
        return null;
      }
      // File.SetLastWriteTimeUtc(지금): 접근 시간은 그대로
      const full = getFullPath(p);
      try { const st = big(full, false); fs.utimesSync(full, msMid(msOf(st.atimeNs)), msMid(Date.now())); } catch (e) { throw fsFail(e, full); }
      return null;
    }),
    createdtime: fileTime('c'),
    modifiedtime: fileTime('m'),
    accessedtime: fileTime('a'),
    isreadonly: one((p) => { if (!fileExists(p)) throw fail(`파일을 찾을수 없습니다: '${p}'`); return isReadOnly(fullPathCore(p)); }),
    setreadonly: (a, name) => {
      need(a, 2, name);
      const p = text(a, 0, name);
      const readOnly = truthy(a[1]);
      if (!fileExists(p)) throw fail(`파일을 찾을수 없습니다: '${p}'`);
      setReadOnly(fullPathCore(p), readOnly);
      return null;
    },
  });
  const listing = (kind) => (a, name) => {
    let path;
    if (a.length > 0 && a[0] === null) throw nullError();
    if (a.length > 0 && typeof a[0] === 'string') { if (!dirExists(a[0])) return false; path = a[0]; } else path = cwd();
    const pattern = a.length > 1 && a[1] !== null ? text(a, 1, name) : '*';
    const recursive = a.length > 2 && truthy(a[2]);
    return strings(enumerate(path, pattern, kind, recursive));
  };
  add('directory.', {
    now: () => cwd(),
    cd: one((p) => {
      if (p.length === 0) throw emptyArg('path');
      const full = getFullPath(p);
      needFs();
      try { sys.chdir(full); } catch (e) { throw fsFail(e, full, true); }
      return cwd();
    }),
    exist: (a) => dirExists(legacyPath(a, '경로를 입력해주세요.')),
    create: one((p) => { const full = argPath(p); createDirectory(full); return full; }),
    remove: (a, name) => {
      need(a, 1, name);
      const p = text(a, 0, name);
      if (!dirExists(p)) return false;
      removeDirectory(fullPathCore(p), a.length > 1 && truthy(a[1]));
      return true;
    },
    files: listing('f'),
    list: listing('d'),
    entries: listing('e'),
    move: (a) => {
      if (a.length >= 2 && typeof a[0] === 'string' && typeof a[1] === 'string') { moveDirectory(a[0], a[1]); return true; }
      throw err('기존 경로, 그리고 이동할 새 경로를 입력하지 않았습니다.');
    },
    copy: (a, name) => { need(a, 2, name); const s = text(a, 0, name), d = text(a, 1, name); copyDirectory(s, d, a.length > 2 && truthy(a[2]), name); return true; },
    parent: one((p) => { const full = argPath(p); return P.getDirectoryName(full); }),
    root: one((p) => P.getPathRoot(getFullPath(p))),
    isempty: one((p) => {
      if (!dirExists(p)) throw fail(`디렉토리를 찾을수 없습니다: '${p}'`);
      let empty = true;
      eachEntry(trimEndSep(fullPathCore(p)), () => { empty = false; return false; });
      return empty;
    }),
    createdtime: dirTime('c'),
    modifiedtime: dirTime('m'),
    accessedtime: dirTime('a'),
  });
}
