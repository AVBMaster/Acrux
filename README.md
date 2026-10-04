# Acrux

一个从零构建的现代浏览器引擎，使用 C# 编写，基于 SkiaSharp 渲染，支持多引擎 JavaScript 执行。

# 停更通知

由于本人学业和工作原因，Acrux 项目将暂停维护约半年，期间可以正常提交issue和pr，项目恢复后将继续维护，每一条issue和pr都会得到回复。感谢所有关注和支持的朋友们！

## 项目结构（旧版概览，新版在legacy算法的基础上添加了大量LayoutNG代码，暂时未更新文档，仅供参考）

```
Acrux/
├── Acrux.Core/              # 核心引擎
│   ├── Css/                     # CSS 解析器（自定义）、选择器、层叠解析、变量、动画
│   ├── Dom/                     # DOM 实现（基于 AngleSharp 解析 + 自定义 DOM 树）
│   ├── JavaScript/              # JS 引擎适配层（V8 / Jint / Jurassic 三引擎支持）
│   ├── Layout/                  # 布局引擎（Block / Inline / Flexbox / Table / Grid / 定位 / 浮动 / 多列）
│   │   └── Grid/                # CSS Grid 布局算法
│   ├── Performance/             # 性能子系统
│   │   ├── Scheduling/          # 协作调度器、空闲回调、长任务观察
│   │   ├── Diagnostics/         # Web Vitals 指标（FCP/LCP/CLS/TBT/FID/TTI）
│   │   ├── Memory/              # 内存压力监控、对象池
│   │   ├── Resources/           # 资源缓存、优先级队列、流式 HTTP 获取
│   │   ├── Compositor/          # 瓦片管理器、瓦片栅格化
│   │   └── Rendering/           # 增量布局引擎、共享样式缓存、选择器索引
│   ├── Network/                 # HTTP 客户端、CookieJar、CORS、重定向处理
│   ├── Input/                   # 输入状态管理
│   ├── EventLoop/               # 事件循环（主线程任务调度）
│   ├── Process/                 # 标签页进程指标
│   └── Fonts/                   # 字体管理
├── Acrux.Rendering/         # 渲染层
│   ├── SkiaRenderer.cs          # SkiaSharp 渲染器（CPU + GPU OpenGL）
│   ├── ChromeRenderer.cs        # 浏览器 Chrome UI（标签栏、地址栏、按钮、状态栏）
│   ├── PaintVisitor.cs          # Paint 操作生成器
│   ├── PaintOps.cs              # Paint 指令定义
│   ├── PaintLayer.cs            # 图层系统（层叠上下文 + z-order）
│   ├── TiledCompositor.cs       # 基于瓦片的合成器
│   ├── ScrollManager.cs         # 滚动管理
│   ├── GradientRenderer.cs      # 渐变渲染
│   ├── FilterRenderer.cs        # CSS 滤镜渲染
│   ├── SkiaTextMeasurer.cs      # Skia 文本测量
│   ├── FontHelper.cs            # 字体回退链
│   ├── RenderingSettings.cs     # 渲染设置
│   ├── RenderingSettingsPage.cs # 设置页面 UI
│   ├── TaskManagerPage.cs       # 任务管理器页面 UI
│   └── DevTools/                # 开发者工具
├── Acrux.Platform/          # 平台抽象层
│   ├── IWindow.cs               # 窗口接口
│   ├── PlatformFactory.cs       # 平台工厂（自动选择 Windows/Linux/macOS）
│   ├── InputManager.cs          # 输入事件管理
│   ├── Clipboard.cs             # 剪贴板（Win32 / xclip / pbcopy）
│   ├── ImeHandler.cs            # 输入法引擎接口
│   ├── Windows/                 # Windows Win32 实现（窗口、IME、原生 API）
│   ├── Linux/                   # Linux X11 实现
│   └── Mac/                     # macOS Cocoa 实现
├── Acrux.Native/            # 原生互操作
│   ├── Windows/Imm32Interop.cs  # Windows IME P/Invoke
│   ├── Linux/ImeBridge.cs       # Linux IME
│   └── macOS/TextInputClient.cs # macOS 文本输入
├── Acrux.Input/             # 输入法模块
│   └── InputMethod.cs
├── Acrux.Core.Tests/        # 单元测试（DOM / CSS / 布局 / Paint / 性能）
├── Acrux.PerfSmokeTest/     # 性能冒烟测试
├── Acrux/                   # 桌面应用入口
│   ├── Program.cs               # 入口点
│   ├── BrowserApp.cs            # 应用主逻辑（窗口管理、导航、渲染循环）
│   ├── InputHandler.cs          # 输入事件路由
│   └── Process/                 # 多进程标签页管理
└── test_css_features.html       # CSS 特性综合测试页面
```

## 架构流水线

```
HTML → AngleSharp 解析 → 自定义 DOM 树 → StyleComputer（层叠解析）
                                                    ↓
                                           LayoutEngine（布局计算）
                                                    ↓
                                           PaintVisitor（生成 Paint 操作）
                                                    ↓
                                           DisplayList → SkiaRenderer / TiledCompositor
                                                    ↓
                                           ChromeRenderer（浏览器 UI）+ DevTools
                                                    ↓
                                           Platform::IWindow（显示输出）
```

## 功能特性

### HTML / DOM
- 使用 AngleSharp 解析 HTML，转换为自定义高性能 DOM 树
- 完整的 DOM API 暴露给 JavaScript（`getElementById`、`querySelector`、`getComputedStyle` 等）
- 支持 `MutationObserver`、`Selection`、`Range`、`TreeWalker`、`NodeIterator`
- Shadow DOM（实验性）

### CSS 引擎
- **选择器**：标签、类、ID、属性选择器、组合器（` `>` `+` `~` `||`）、命名空间
- **伪类**：`:hover`、`:focus`、`:disabled`、`:checked`、`:first-child`、`:last-child`、`:nth-child()`、`:not()`、`:is()`、`:where()`、`:has()`、`:empty`、`:root`、`:link`、`:visited`、`:target`
- **伪元素**：`::before`、`::after`、`::first-line`、`::first-letter`、`::placeholder`
- **@规则**：`@media`、`@supports`、`@layer`、`@keyframes`、`@font-face`、`@import`、`@page`
- **层叠**：特异性计算、`!important`、内联样式、`@layer`、源顺序
- **变量**：CSS 自定义属性，循环检测
- **简写展开**：`margin`、`padding`、`border`、`background`、`font`、`flex`、`text-decoration`、`animation`、`transition`
- **函数**：`calc()`、`min()`、`max()`、`clamp()`、`var()`
- **颜色**：HEX、RGB/RGBA、HSL/HSLA、HWB、LAB、LCH、OKLAB、OKLCH、`color()`、147 命名色
- **动画**：`@keyframes` 解析、动画状态管理

### 布局引擎

| 布局模式     | 状态 | 说明 |
|-------------|------|------|
| Block       | ✓    | 外边距折叠、百分比宽度、auto 尺寸、box-sizing |
| Inline      | ✓    | 文本行构建、自动换行、inline-block、text-align、word-break、white-space |
| Flexbox     | ✓    | flex-direction、wrap、grow/shrink/basis、justify-content、align-items、align-self、gap |
| Table       | ✓    | auto/fixed 布局、行/列组、表格单元格分发 |
| Grid        | ✓    | grid-template-columns/rows、grid-template-areas、隐式轨道、间距 |
| 定位        | ✓    | static、relative、absolute、fixed、sticky（滚动约束偏移模型） |
| Float       | ✓    | 浮动盒子放置、clear 清除 |
| 多列        | ✓    | column-count/width、column-gap、column-rule |
| 列表项      | ✓    | 列表标记渲染 |
| 溢出        | ✓    | hidden、scroll、auto、overflow: clip |

### 渲染

- **背景**：颜色、线性/径向渐变、图像
- **边框**：样式、颜色、圆角（border-radius）
- **盒阴影**：内外阴影
- **轮廓**：outline 渲染
- **文本装饰**：underline、overline、line-through
- **变换**：matrix、translate、rotate、scale、skew
- **滤镜**：blur、brightness、contrast、grayscale、sepia、hue-rotate、invert、drop-shadow、opacity、saturate
- **裁剪路径**：circle、ellipse、polygon、inset
- **backdrop-filter**
- **不透明度 + 合成模式**
- **GPU 加速**：OpenGL（SkiaSharp GRContext），自动降级到 CPU
- **瓦片合成**：256px 瓦片、脏矩形跟踪、LRU 淘汰、预测预栅格化
- **SKPicture 缓存**：缓存页面内容实现高效滚动合成

### JavaScript 引擎

支持三种引擎，通过 `JavaScriptEngineSwitcher.Core` 适配：

| 引擎 | 类型 | ES 支持 | 说明 |
|------|------|---------|------|
| **V8** (ClearScript) | 原生 | ES2020+ | 默认引擎，性能最佳 |
| **Jint** | 纯 .NET | ES5.1 | 跨平台后备引擎 |
| **Jurassic** | 纯 .NET | ES5 | 轻量备选 |

- `setTimeout` / `setInterval` / `requestAnimationFrame`
- `fetch` / `XMLHttpRequest`
- `console` API（log、error、warn、info、debug、trace、table、group、count、time、assert）
- `alert` / `confirm` / `prompt`（UI 弹窗）
- DOM 事件（click、keydown、keyup、submit、change 等）
- `localStorage` / `sessionStorage`
- `history.pushState` / `replaceState`
- `window.getComputedStyle`、`element.getBoundingClientRect`、`element.matches` 等

### 性能子系统

- **协作调度器**：优先级队列、帧预算、可取消/可让步任务
- **空闲回调调度器**
- **长任务观察**：50ms 阈值检测
- **Web Vitals 指标**：FCP、LCP、TBT、CLS、FID、TTI
- **内存压力监控**：3GB 阈值、自动响应
- **对象池**：Paint 操作复用、减少 GC
- **资源缓存**：LRU 淘汰策略
- **增量布局引擎**：脏标志传播、布局缓存跳过已清理子树
- **共享样式缓存**：跨元素共享 `ComputedStyle`
- **选择器索引**：基于类/ID/标签名的快速匹配
- **时钟**：纳秒级单调计时器、`TimingScope`、`TimingAccumulator`
- **空间网格索引**：O(1) DisplayList 查找
- **SKPicture 页面缓存**：合成滚动

### 网络

- HTTP/HTTPS 请求（`StreamingHttpFetcher`）
- CookieJar（过期、域/路径匹配、Secure/HttpOnly/SameSite）
- CORS 策略检查
- 重定向跟踪（307/308/301/302/303）
- User-Agent：Chrome 132 兼容

### 平台支持

| 平台 | 状态 | 窗口系统 |
|------|------|---------|
| Windows | ✓ 完整支持 | Win32 API |
| Linux | ✓ 实验性 | X11 |
| macOS | ✓ 实验性 | Cocoa (ObjC) |

### 开发者工具

- **Sources**：HTML 源码查看与编辑，实时预览
- **Console**：JS 交互式控制台
- **Elements**：DOM 元素结构查看
- **主题**：支持亮/暗主题切换

### IME 输入法支持

- Windows IMM32 / TSF 兼容
- 组合窗口定位（跟随光标）
- 候选项窗口
- 中/日/韩文输入

## 构建要求

- .NET 10.0 SDK
- Windows（主要开发平台）
- SkiaSharp（NuGet）
- JavaScriptEngineSwitcher.*（NuGet）

## 构建 & 运行

```bash
# 构建所有项目
dotnet build

# 运行浏览器
dotnet run --project Acrux

# 运行测试
dotnet test
```

浏览器启动后会自动加载内置测试页面（`test_css_features.html`）。

### 快捷键

| 按键 | 功能 |
|------|------|
| `F12` | 打开/关闭开发者工具 |
| `Shift+Esc` | 打开/关闭任务管理器 |
| `F5` | 刷新页面 |
| `Ctrl+L` | 聚焦地址栏 |
| `Ctrl+T` | 新建标签页 |
| `Ctrl+W` | 关闭当前标签页 |
| `Tab` | 切换标签页 |
| `↑/↓/←/→` | 页面滚动 |
| `PageUp/PageDown` | 翻页滚动 |
| `Home/End` | 滚动到顶部/底部 |

## 开发计划

- [ ] 完善 CSS Grid 布局（Grid 算法已有，需要更多测试）
- [ ] 支持更多 CSS 属性
- [ ] 性能优化（JS JIT、增量渲染）
- [ ] 网络栈增强（HTTP/2、WebSocket）
- [ ] 扩展 DevTools 功能
- [ ] 完善 Linux/macOS 平台支持
- [ ] 多进程标签页隔离

## 贡献

浏览器引擎的开发是一个复杂且长期的过程，欢迎社区贡献代码和提出建议！

如有任何问题或建议，请提交 Issue 或 Pull Request。

## 许可证

MIT License
