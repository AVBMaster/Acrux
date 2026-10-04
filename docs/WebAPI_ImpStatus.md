# Acrux WebAPI 实现对照文档

> 对照 MDN Web API 标准，整理 Acrux 当前的实现状态。
> 状态标记：`✅ 已实现` / `⚠️ 部分实现（骨架/空壳/仅属性）` / `❌ 未实现` / `🚫 暂不需要（浏览器无关）`

---

## 一、规范级别总览

| 规范名称 | 状态 | 说明 |
| :--- | :--- | :--- |
| Console API | ✅ | `console.log/error/warn/info/debug/trace/dir/table/group/groupEnd/clear/count/time/timeEnd` 全部实现 |
| Fetch API | ✅ | `fetch(url, options)` 基于 Promise，支持 GET/POST/PUT/PATCH，Headers 和 body |
| XMLHttpRequest API | ✅ | `open/setRequestHeader/send/abort/getResponseHeader/getAllResponseHeaders`，事件回调完整 |
| Web Storage API | ✅ | `localStorage` / `sessionStorage`，`getItem/setItem/removeItem/clear/key/length` |
| History API | ✅ | `pushState/replaceState/back/forward/go/length/state` |
| URL API | ✅ | `URL` 构造函数 + `href/origin/protocol/hostname/port/pathname/search/hash/searchParams` |
| URLSearchParams API | ✅ | `URLSearchParams` 完整实现 |
| HTML DOM API | ✅ | 大部分 HTML 元素类型已实现（见 DOM 章节） |
| Document Object Model (DOM) | ✅ | `getElementById/querySelector/querySelectorAll/createElement` 等 |
| Canvas API | ⚠️ | Canvas 2D 上下文**完整接口**已定义，但**渲染方法均为空壳**（`{}` 无实际绘制） |
| CSS Object Model (CSSOM) | ⚠️ | `getComputedStyle` 返回静态快照；`CSSStyleDeclaration` / `style` 属性可读写 |
| CSSOM view API | ⚠️ | `getComputedStyle` 可用；`window.visualViewport` 未实现 |
| CSS Font Loading API | ❌ | 未实现 `FontFace` / `FontFaceSet` 暴露到 JS |
| CSS Typed Object Model API | ❌ | 未实现 |
| Document Picture-in-Picture API | ❌ | 未实现 |
| EditContext API | ❌ | 未实现 |
| Encrypted Media Extensions API | ❌ | 未实现 |
| Event API (UI Events) | ✅ | `Event/CustomEvent/MouseEvent` 构造函数，`addEventListener/removeEventListener/dispatchEvent` |
| File API | ⚠️ | `Blob` 类已定义（骨架），`FileReader` 未实现 |
| Fullscreen API | ❌ | `fullscreenEnabled/exitFullscreen` 返回 false |
| Geometry interfaces | ⚠️ | `DOMRect/DOMPoint/DOMMatrix` 已定义（见 DOM） |
| HTML Drag and Drop API | ❌ | 未实现 |
| HTML Sanitizer API | ❌ | 未实现 |
| IndexedDB API | ❌ | 未实现 |
| Intersection Observer API | ⚠️ | 类已定义（`IntersectionObserver/Init/Entry`），但回调为空 |
| MutationObserver API | ✅ | 完整实现，支持 `childList/attributes/characterData/subtree/attributeOldValue/characterDataOldValue/attributeFilter` |
| Page Visibility API | ⚠️ | `document.hidden`/`visibilityState` 已暴露（静态值） |
| Performance API | ⚠️ | `PerformanceMetrics` 内部有完整实现（FP/FCP/LCP/TBT/CLS/FID）；**`window.performance` 未暴露到 JS** |
| Pointer Lock API | ❌ | 未实现 |
| Popover API | ❌ | 未实现 |
| Prioritized Task Scheduling API | ⚠️ | `requestIdleCallback/cancelIdleCallback` 已暴露（用 setTimeout 模拟） |
| Resize Observer API | ⚠️ | 类已定义（`ResizeObserver/Options/Entry/Size`），但 `observe()` 为空 |
| Screen Orientation API | ⚠️ | `screen.orientation` 已暴露（静态值） |
| Selection API | ❌ | `getSelection()` 返回 null |
| Service Worker API | ❌ | 类已定义但为空壳 |
| Streams API | ❌ | 未实现 |
| SVG API | ⚠️ | SVG 元素类型已定义，但属性/方法多数为空 |
| UI Events | ✅ | `MouseEvent/Event` 构造函数及属性已实现 |
| URL Fragment Text Directives | ❌ | 未实现 |
| User-Agent Client Hints API | ❌ | 未实现 |
| WebSocket API | ❌ | 未实现 |
| Web Workers API | ❌ | 未实现 |
| Web Audio API | ❌ | 未实现 |
| Web Animations API | ❌ | 未实现 |
| Web Crypto API | ❌ | 未实现 |
| Web RTC API | ❌ | 未实现 |
| WebGPU API | ❌ | 未实现 |
| WebGL: 2D and 3D graphics | ⚠️ | `WebGLRenderingContext` 类已定义但为空壳 |
| XMLHttpRequest API | ✅ | 已实现（详见上） |
| Notification API | ❌ | 未实现 |
| Push API | ❌ | 未实现 |
| Beacon API | ❌ | 未实现 |
| Clipboard API | ❌ | 未实现 |
| Geolocation API | ❌ | 未实现 |
| Media Stream API | ❌ | 未实现 |
| Screen Capture API | ❌ | 未实现 |
| Web Share API | ❌ | 未实现 |
| Web Speech API | ❌ | 未实现 |
| Payment Request API | ❌ | 未实现 |
| Web Locks API | ❌ | 未实现 |
| Encoding API | ❌ | 未实现 |
| Web Components | ❌ | 未实现（`CustomElementRegistry` 未暴露） |

---

## 二、Window 全局属性与方法

| 属性/方法 | 状态 | 实现位置 |
| :--- | :--- | :--- |
| `window` / `globalThis` / `self` | ✅ | `GetSetupScript` |
| `innerWidth` / `innerHeight` | ✅ | 动态绑定到浏览器窗口尺寸 |
| `outerWidth` / `outerHeight` | ✅ | 同 innerWidth/innerHeight |
| `devicePixelRatio` | ✅ | 动态绑定 |
| `pageXOffset` / `pageYOffset` / `scrollX` / `scrollY` | ✅ | 动态绑定 |
| `screenX` / `screenY` / `screenLeft` / `screenTop` | ⚠️ | 静态返回 0 |
| `closed` / `opener` / `parent` / `top` / `frames` / `length` | ⚠️ | 静态返回默认值 |
| `status` / `defaultStatus` / `name` | ⚠️ | 静态返回空字符串 |
| `alert()` | ✅ | `WindowHost.alert` → 对话框 |
| `confirm()` | ✅ | `WindowHost.confirm` → 对话框 |
| `prompt()` | ✅ | `WindowHost.prompt` → 对话框 |
| `setTimeout()` / `setInterval()` / `clearTimeout()` / `clearInterval()` | ✅ | 定时器管理完整 |
| `requestAnimationFrame()` / `cancelAnimationFrame()` | ⚠️ | 用 setTimeout 模拟（16ms 间隔） |
| `requestIdleCallback()` / `cancelIdleCallback()` | ⚠️ | 用 setTimeout 模拟（50ms 延迟） |
| `fetch()` | ✅ | Promise 封装，`_fetch` 后端调用 |
| `XMLHttpRequest` | ✅ | `createXMLHttpRequest()` |
| `URL()` | ✅ | `createURL()` |
| `URLSearchParams()` | ✅ | `createURLSearchParams()` |
| `parseInt()` / `parseFloat()` / `isNaN()` / `isFinite()` | ✅ | 内置函数 |
| `decodeURI()` / `decodeURIComponent()` / `encodeURI()` / `encodeURIComponent()` | ✅ | 内置函数 |
| `escape()` / `unescape()` | ✅ | 内置函数 |
| `atob()` / `btoa()` | ✅ | 内置函数 |
| `Image()` | ✅ | 创建 `document.createElement('img')` |
| `CustomEvent()` | ✅ | 构造函数 |
| `MouseEvent()` | ✅ | 构造函数 |
| `scrollTo()` / `scrollBy()` / `scroll()` | ⚠️ | 有方法但仅调用回调 |
| `getComputedStyle()` | ✅ | 调用 `document.getComputedStyle()` |
| `matchMedia()` | ⚠️ | 返回静态 `{matches:true}` 对象 |
| `open()` | ⚠️ | 在 location.href 中导航 |
| `close()` / `print()` / `stop()` / `focus()` / `blur()` | ⚠️ | 空方法 |
| `moveBy()` / `moveTo()` / `resizeBy()` / `resizeTo()` | ⚠️ | 空方法 |
| `postMessage()` | ⚠️ | 空方法 |
| `getSelection()` | ⚠️ | 返回 null |
| `addEventListener()` / `removeEventListener()` | ✅ | 转发到 document |
| `Promise.allSettled()` / `Promise.any()` | ⚠️ | 手动 polyfill（仅当 JS 引擎不支持时） |

---

## 三、Navigator

| 属性/方法 | 状态 | 值 |
| :--- | :--- | :--- |
| `navigator.userAgent` | ⚠️ | 静态 "Acrux/1.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" |
| `navigator.appCodeName` | ⚠️ | "Mozilla" |
| `navigator.appName` | ⚠️ | "Acrux" |
| `navigator.appVersion` | ⚠️ | "1.0" |
| `navigator.platform` | ⚠️ | "Win32" |
| `navigator.product` | ⚠️ | "Gecko" |
| `navigator.language` | ⚠️ | "zh-CN" |
| `navigator.languages` | ⚠️ | `["zh-CN", "en"]` |
| `navigator.cookieEnabled` | ⚠️ | true |
| `navigator.onLine` | ⚠️ | true |
| `navigator.javaEnabled()` | ⚠️ | false |
| `navigator.serviceWorker` | ❌ | 未实现 |
| `navigator.clipboard` | ❌ | 未实现 |
| `navigator.geolocation` | ❌ | 未实现 |
| `navigator.mediaDevices` | ❌ | 未实现 |
| `navigator.mediaCapabilities` | ❌ | 未实现 |

---

## 四、Location

| 属性/方法 | 状态 | 说明 |
| :--- | :--- | :--- |
| `href` | ✅ | 可读写，写入触发导航 |
| `protocol` / `hostname` / `host` / `port` | ⚠️ | 静态/可写 |
| `pathname` / `search` / `hash` / `origin` | ⚠️ | 静态/可写 |
| `assign()` / `replace()` | ✅ | 触发导航事件 |
| `reload()` | ✅ | 触发 reload 事件 |

---

## 五、History

| 方法 | 状态 | 说明 |
| :--- | :--- | :--- |
| `pushState()` | ✅ | 改变 history 栈和 location |
| `replaceState()` | ✅ | 替换当前 history 条目 |
| `back()` / `forward()` | ⚠️ | 仅改变内部索引，未触发实际导航 |
| `go(delta)` | ⚠️ | 仅改变内部索引 |
| `length` | ✅ | history 栈大小 |
| `state` | ✅ | 当前 state 对象 |

---

## 六、Screen

| 属性 | 状态 | 值 |
| :--- | :--- | :--- |
| `width` / `height` | ⚠️ | 静态 1920x1080 |
| `availWidth` / `availHeight` / `availTop` / `availLeft` | ⚠️ | 静态值 |
| `colorDepth` / `pixelDepth` | ⚠️ | 静态 24 |
| `orientation` | ⚠️ | 返回 `ScreenOrientationHost`（静态） |

---

## 七、Storage (localStorage / sessionStorage)

| 方法/属性 | 状态 | 说明 |
| :--- | :--- | :--- |
| `getItem(key)` | ✅ | 完整实现 |
| `setItem(key, value)` | ✅ | 完整实现 |
| `removeItem(key)` | ✅ | 完整实现 |
| `clear()` | ✅ | 完整实现 |
| `key(index)` | ✅ | 完整实现 |
| `length` | ✅ | 完整实现 |
| 持久化 | ❌ | 数据仅内存中，无跨会话持久化 |

---

## 八、DOM 核心

### Document 主要方法

| 方法/属性 | 状态 | 说明 |
| :--- | :--- | :--- |
| `getElementById()` | ✅ | |
| `querySelector()` | ✅ | |
| `querySelectorAll()` | ✅ | |
| `getElementsByTagName()` | ✅ | |
| `getElementsByClassName()` | ✅ | |
| `getElementsByName()` | ✅ | |
| `createElement()` | ✅ | |
| `createElementNS()` | ✅ | |
| `createTextNode()` | ✅ | |
| `createComment()` | ✅ | |
| `createDocumentFragment()` | ✅ | |
| `createAttribute()` | ✅ | |
| `createEvent()` | ⚠️ | 支持 `customevent`/`mouseevent` 类型 |
| `addEventListener()` | ✅ | |
| `removeEventListener()` | ✅ | |
| `dispatchEvent()` | ✅ | |
| `getComputedStyle()` | ✅ | 返回 ComputedStyleHost |
| `write()` / `writeln()` | ⚠️ | 写入 body |
| `documentElement` / `body` / `head` | ✅ | |
| `title` | ✅ | 可读写 |
| `cookie` | ⚠️ | 静态字典，无 cookie 解析/过期 |
| `URL` / `documentURI` / `baseURI` | ✅ | |
| `readyState` | ⚠️ | 静态 "complete" |
| `domain` / `referrer` | ⚠️ | 静态默认值 |
| `hidden` / `visibilityState` | ⚠️ | 静态值 |
| `forms` / `images` / `links` / `scripts` / `anchors` | ✅ | 集合返回 |
| `activeElement` | ⚠️ | 返回静态元素 |
| `implementation` | ⚠️ | 空对象 |

### Element 主要方法

| 方法/属性 | 状态 |
| :--- | :--- |
| `tagName` / `nodeName` / `nodeValue` | ✅ |
| `textContent` / `innerText` | ✅ |
| `innerHTML` / `outerHTML` | ✅ |
| `className` / `classList` | ✅ |
| `getAttribute()` / `setAttribute()` / `hasAttribute()` / `removeAttribute()` | ✅ |
| `attributes` / `dataset` | ✅ |
| `children` / `childNodes` / `firstChild` / `lastChild` | ✅ |
| `parentNode` / `previousSibling` / `nextSibling` | ✅ |
| `parentElement` | ✅ |
| `getBoundingClientRect()` | ⚠️ | 返回布局计算后的矩形 |
| `getClientRects()` | ⚠️ | 返回空数组 |
| `getComputedStyle()` | ✅ | |
| `matches()` | ✅ | |
| `closest()` | ✅ | |
| `contains()` | ✅ | |
| `appendChild()` / `insertBefore()` / `removeChild()` / `replaceChild()` | ✅ |
| `cloneNode()` | ✅ | |
| `querySelector()` / `querySelectorAll()` | ✅ | |
| `getElementsByClassName()` / `getElementsByTagName()` | ✅ | |
| `style` | ✅ | 读写（StyleHost，驼峰→短横线自动转换） |
| `addEventListener()` / `removeEventListener()` | ✅ | |

### HTML 元素

已实现（带特定属性）：
- `HTMLAnchorElement` (href, target, text, download, hrefLang, protocol, host, hostname, port, username, password, hash, search, pathname, origin)
- `HTMLButtonElement` (type, value, form, disabled)
- `HTMLDivElement`
- `HTMLFormElement` (action, method, target, acceptCharset)
- `HTMLIFrameElement` (src, width, height)
- `HTMLImageElement` (src, width, height, alt, naturalWidth, naturalHeight, complete, sizes, srcset)
- `HTMLInputElement` (type, value, placeholder, disabled, checked, readOnly, required, min, max, step, pattern, name, files, selectionStart, selectionEnd)
- `HTMLLabelElement` (htmlFor)
- `HTMLLinkElement` (href, rel, type, media)
- `HTMLMetaElement` (name, content, charset)
- `HTMLScriptElement` (src, type, async, defer)
- `HTMLSelectElement` (value, selectedIndex, multiple, options)
- `HTMLTextAreaElement` (value, placeholder, rows, cols, readOnly, required)
- `HTMLUListElement` / `HTMLOListElement` / `HTMLLIElement`
- `HTMLTableElement` / `HTMLTableRowElement` / `HTMLTableCellElement`
- `HTMLCanvasElement` (GetContext, ToDataURL, ToBlob)
- `HTMLOptionElement`
- `HTMLOutputElement`
- `HTMLSlotElement`
- `HTMLTitleElement`

---

## 九、Event 系统

| 接口/类 | 状态 | 说明 |
| :--- | :--- | :--- |
| `Event` | ✅ | 基础事件类 |
| `EventTarget` | ✅ | `addEventListener/removeEventListener/dispatchEvent` |
| `CustomEvent` | ✅ | JS 构造函数 |
| `MouseEvent` | ✅ | JS 构造函数（clientX/Y, screenX/Y, button, ctrl/shift/alt/metaKey） |
| `ScriptEvent` | ✅ | 内部事件分发 |
| `MutationObserver` | ✅ | 完整实现 |
| `IntersectionObserver` | ⚠️ | 类已定义，回调为空 |
| `ResizeObserver` | ⚠️ | 类已定义，observe 为空 |

---

## 十、Canvas

| 属性/方法 | 状态 | 说明 |
| :--- | :--- | :--- |
| `HTMLCanvasElement.GetContext("2d")` | ✅ | 返回 CanvasRenderingContext2D |
| `HTMLCanvasElement.GetContext("webgl")` | ⚠️ | 返回空壳 WebGLRenderingContext |
| `HTMLCanvasElement.GetContext("webgl2")` | ⚠️ | 返回空壳 WebGLRenderingContext |
| `CanvasRenderingContext2D` 所有方法 | ⚠️ | **接口签名完整，但所有方法体为空**（Save/Restore/Transform/DrawImage/Text/PutImageData 等均为 `{}`） |
| `ImageData` | ✅ | 数据结构完整 |
| `Path2D` | ⚠️ | 接口签名完整，方法为空 |
| `CanvasGradient` / `CanvasPattern` | ⚠️ | 空类 |
| `TextMetrics` | ⚠️ | 属性全为 0 |
| `ToDataURL()` / `ToBlob()` | ⚠️ | 返回空/占位 |

---

## 十一、CSS

| 接口/类 | 状态 | 说明 |
| :--- | :--- | :--- |
| `CSSStyleDeclaration` (element.style) | ✅ | 读写 CSS 属性，驼峰↔短横线自动转换 |
| `getComputedStyle()` | ✅ | 返回 ComputedStyleHost，含常用属性 |
| `CSSStyleSheet` | ❌ | 未暴露到 JS |
| `CSSRuleList` | ❌ | 未暴露到 JS |
| `CSSRule` 各子类 | ❌ | 未暴露到 JS |

---

## 十二、Geometry 接口

| 接口 | 状态 | 说明 |
| :--- | :--- | :--- |
| `DOMMatrix` | ✅ | 矩阵操作（translate/scale/rotate/transform/multiply） |
| `DOMPoint` | ✅ | 点 + matrixTransform |
| `DOMRect` | ⚠️ | 基础属性，`union` 等方法为空 |

---

## 十三、核心引擎基础设施

| 组件 | 状态 | 说明 |
| :--- | :--- | :--- |
| JavaScript 引擎 | ✅ | 支持 Jint / V8 / Jurassic，通过 IPC 与独立进程通信 |
| 定时器系统 | ✅ | setTimeout/setInterval/clearTimeout/clearInterval |
| Promise 封装 | ✅ | fetch 返回 Promise |
| DevTools 控制台 | ✅ | console → DevToolsConsole 输出 |
| 回调管理 | ✅ | 函数 ID 存储与调用 |
| 远程引擎 IPC | ✅ | MmapTransport 通信 |
| 错误处理 | ✅ | OnScriptError 事件 |

---

## 十四、开发优先级建议

### 高优先级（影响常见 Web 页面加载）
1. **`window.performance` 暴露** — `PerformanceMetrics` 已有完整实现，只需包装暴露到 JS
2. **Canvas 2D 实际渲染** — 当前接口完整但方法为空，需要对接 SkiaSharp
3. **Storage 持久化** — 数据跨会话/跨标签保持
4. **MutationObserver 集成到 JS** — C# 已实现，但需注册到 JS 全局

### 中优先级（影响交互体验）
5. **Fetch API 完善** — 添加 `Response.ok`、`Response.status`、`Request` 对象、`Headers` 对象
6. **History 实际导航** — `back()`/`forward()` 应触发页面跳转
7. **IntersectionObserver 回调** — 实现可见性检测
8. **ResizeObserver 回调** — 实现尺寸变化检测
9. **document.cookie 完善** — 添加 Path/Domain/Expires 解析

### 低优先级（高级/罕见场景）
10. WebSocket 实现
11. Web Workers 实现
12. Web Audio API
13. Service Workers
14. Web Crypto API

---

## 十五、文件映射

| 实现文件 | 对应 WebAPI |
| :--- | :--- |
| `Acrux.Core/JavaScript/JavaScriptEngine.cs` | Window 全局、Navigator、Location、History、Screen、Storage、Console、Fetch、Builtins |
| `Acrux.Core/JavaScript/URLHost.cs` | URL、URLSearchParams |
| `Acrux.Core/JavaScript/XMLHttpRequestHost.cs` | XMLHttpRequest |
| `Acrux.Core/JavaScript/DocumentHost.cs` | Document、HTMLElement |
| `Acrux.Core/JavaScript/ElementHost.cs` | Element、EventTarget、DOMTokenList |
| `Acrux.Core/JavaScript/StyleHost.cs` | CSSStyleDeclaration |
| `Acrux.Core/Dom/Html/HTMLCanvasElement.cs` | Canvas API |
| `Acrux.Core/Dom/MutationObserver/MutationObserver.cs` | MutationObserver |
| `Acrux.Core/Dom/Html/Observers.cs` | IntersectionObserver、ResizeObserver |
| `Acrux.Core/Dom/Html/Fullscreen.cs` | Fullscreen API、ServiceWorker（空壳） |
| `Acrux.Core/Dom/Events/*.cs` | Event、EventTarget、MouseEvent、TouchEvent、WheelEvent、KeyboardEvent |
| `Acrux.Core/Dom/Geometry/*.cs` | DOMMatrix、DOMPoint、DOMRect |
| `Acrux.Core/Performance/Diagnostics/PerformanceApi.cs` | Performance API（内部） |
| `Acrux.Core/Performance/Diagnostics/PerformanceMetrics.cs` | PerformanceMetrics（内部） |
