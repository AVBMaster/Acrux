# Acrux CSS 标准验证 · 交接文档

> 面向：在另一台电脑上接手本仓库 CSS 标准验证与引擎修复的 AI/工程师。
> 本文自包含：不依赖任何外部"记忆"，所有已验证结论、判据数据、工具用法、踩过的坑都写在这里。
> 最后更新：2026-10-04（b182/b183/b184 批次之后）。

---

## 0. 一分钟上手（硬性约束，先读）

| 事项 | 约束 |
|---|---|
| 产品名 | **Acrux**（原 UpBrowser，已全仓改名）。引擎品牌：Cruxism(核心)/Aurora(布局)/Prism(渲染)，但命名空间仍是 `Acrux.*` 树，**不要**提升为裸 `Aurora` 根 |
| 构建 | `dotnet build`；运行 `dotnet run --project Acrux`；解决方案是 `Acrux.slnx`（XML 新格式） |
| **禁止** `dotnet test` | 仓库内测试已知有问题（AGENTS.md 明令）。但 **`Acrux.SmokeTest` 是控制台程序，可以跑**：`dotnet run --project Acrux.SmokeTest`，基线 = `ALL SMOKE TESTS PASSED` |
| **禁止** git 写操作 | 不 stage / 不 commit / 不 branch / 不 stash。只读（`git log/show/diff/status`）可以 |
| 语言 | 对话用中文，**代码与注释用英文** |
| 不许删功能 | 除非用户明确要求；过程中发现的既有问题也要专业地修，不能摆着 |
| 参考实现 | 可查 `chromium`，但**代码注释与文档里绝不能出现 blink/miniblink 字样** |
| 门禁二进制 | 无头验证必须走 `Acrux` |

---

## 1. 验证体系：五条通道（缺一不可交叉）

| 通道 | 命令 | 用途 / 注意点 |
|---|---|---|
| 像素对拍 | `Acrux --snapshot <html> <png> [w] [h] [dpi] [timeMs]` + `--diff <a> <b> [out.png] [tol]` | 回归哨兵。`--diff` 输出 `N/M px differ (P%)`。**判据容差 0.5 设备像素**；`tol` 可选 |
| 布局数值 | `--dumplayout <html> [w] [h] [dpi]` | 每个元素盒 `(x,y WxH)` + `lineH` + 行数 + 每行 `line y=… baseline=…` + 每 run `run '文本' x= w= b= fs=`。**第 5 个参数是 dpi，默认 1.0 —— 与 Edge 对照时必须显式传 1.25**（见 §7） |
| 计算样式 | `--computed <html> [w] [h] [dpi]` | **只打印带 `id` 的元素**，每元素十几行（display/border/radius/outline/margin/padding/inset/font+lh/color+bg/shadow/flex/listStyle/spacing/textOverflow/columns…）。用来和 Chrome `getComputedStyle` 逐字段对拍（含简写缺省重置、初值） |
| 显示列表 | `--textops <html> [w] [h] [timeMs]` | 绘制算子列表。行格式是 `[text] '内容' x=… y=…`（**不是** `text=`，用错 grep 模式会静默丢行导致误判"没画"） |
| 像素探针 | `--pixels <png> <y> <x0> <x1>` / `--rows <png> <x0> <x1> <y0> <y1>` | 读已生成 PNG 的某行像素 RGB / 某矩形内非白像素的最左最右。用来证实"填充色/边框宽度/带位置" |
| 动画 | `--anim <html> [w] [h] [timeMs]` | 时间轴快照 |
| 交互/宿主 | `--frameshot` / `--localshot` / `--interact`（在 `Acrux/Program.cs` 分派） | Phase 1b 的进程内宿主 InProcessTabHost 通道：根滚动归宿主；用于命中测试/交互回归 |
| 并发诊断 | `--tabtest` / `--proctest` / `--idletest` + 环境变量 `ACRUX_TAB_STATS` | single/threaded/process 三种标签并发模型的心跳统计 |

### 浏览器 对照（外部真值）
用 `browser-use` MCP：`navigate_page` 打开 `file:///…` → `evaluate_script` 取 `getBoundingClientRect()` / `getComputedStyle()` / `Range` 矩形 → 需要时 `take_screenshot`。

三条铁律（都踩过）：
1. **取行位置必须用"逐词 Range 矩形"**：`Range.setStart(textNode,0)/setEnd(textNode,len)` 后取 rect 再减段落自身 left/top。用整段 `selectNodeContents` 的 min-left 会造出假缺陷（曾据此登记过不存在的 #154）。
2. **对照前必须对齐 dpr**：本机 Edge `devicePixelRatio = 1.25`，而 `--dumplayout` 不传第 5 参就是 1.0。`line-height:normal` 按设备像素量化 → 同一份 CSS 在 1.0 是 21.0、在 1.25 是 21.6，混用会造出假"字体度量缺陷"。探针里顺手回传 `devicePixelRatio` 作凭据。
3. **截图通道可能不可用**（`NATIVE_BROWSER_VIEWPORT_UNAVAILABLE`）。此时用"标记宽度指纹法"：`display:list-item; list-style-position: inside` + Range 量 `<span>` 相对 `<li>` 的 left 偏移，等价于拿到 marker 盒宽。
4. `evaluate_script` 里 `getComputedStyle(el,'::before').content` 返回 Chrome **规范化后**的值（缺省被丢、非法整条 → `none`），非常适合对拍简写解析。
5. **rAF / 长异步 / 子代理** 在 browser-use 通道是死路，别指望。

### 判据纪律
- 像素 ref 只是**回归哨兵**，不是正确性证明：历史 ref 里确实存在"当时已知缺陷"的快照（例：b39 §1 的 spanner 压住下一组首行）。凡涉及新接线，一律以 Edge 数值为准，再重烤 ref。
- 差异百分比异常大时，先怀疑**测量方式**（dpr 猜错 / grep 模式错 / 页面被改过），再怀疑引擎。
- 不能声称未验证的通过；自己引入的回归不许甩锅给"既有问题"。

---

## 2. 测试资产与命名约定

```
snapshots/css-standard-verify<N>[-<topic>].html   测试页（N = 批次号，1..184）
snapshots/out/ref-b<N>[-<topic>].png              已验证参考图（161 张）
snapshots/out/cur-b<N>.png                        本轮当前渲染（临时对比用）
snapshots/out/old-b<N>.png                        重烤前保留的旧图（证据）
snapshots/out/_gate_all.txt                       全量对拍报告（每行一个 ref）
snapshots/probe-*.html / out/_probe_*.html        一次性探针页（不算资产）
```

**批次号 = 任务号体系**：任务清单里 "b166"、"#178" 分别指测试批次与全局任务编号，两者混用是历史习惯，看上下文。

### 辅助脚本（都在 `snapshots/`，Python 3，无第三方依赖）

| 脚本 | 用法 | 作用 |
|---|---|---|
| `marker_gate.py` | `python snapshots/marker_gate.py 166 179` 或 `--all` | 回归门禁：按批次号**精确**配对 ref↔page，逐个 dpr（1.25→1.0→2.0）探测，**首个 0.000% 即停**；输出每行 `b<N><suffix>: N/M px differ (P%) @dpi<X>` |
| `rebake_from_gate.py` | `python snapshots/rebake_from_gate.py [_gate_all.txt]` | 读门禁报告，把非 0 的 ref 按报告里的 dpr 重烤（旧图存 `old-*.png`），**保持 ref 像素尺寸不变** |
| `rebake_ref.py` | `python snapshots/rebake_ref.py 25 34` | 手工重烤指定批次（dpr 由 ref 尺寸猜） |
| `diff_bands.py` | `python snapshots/diff_bands.py a.png b.png` | 输出差异 y 带（判断"整体位移"还是"局部结构变化"） |
| `edges_at.py` | `python snapshots/edges_at.py b.png 200 [400…]` | 列出指定列上颜色突变的 y 及前后 RGB —— **把盒子边缘坐标从图里读出来**，用于核对 ref 与实测 |
| `crop_png.py` | `python snapshots/crop_png.py src.png out.png x0 y0 x1 y1` | 裁 PNG（自己实现解码/编码，看长图局部） |
| `ink_box.py` | `python snapshots/ink_box.py a.png b.png` | 墨迹包围盒（快速判断 dpr 猜错：尺寸比例会整体差 1.25/2 倍） |
| `counter_rows.py` | 见文件头 | 计数样式逐行取数 |
| `sweep.py` | 见文件头 | 早期批量出图 |

⚠️ Python/Node 在本机把 `/tmp/x` 解析成 `D:\tmp\x`（git-bash 的 `/tmp` 是另一处）；跨工具传路径一律用绝对路径或仓库内路径。
⚠️ 含引号/`$_`/反斜杠的脚本**先写成文件再执行**，别用 heredoc（PS 5.1 + bash 混用会炸）。

---

## 3. 已验证的 CSS 能力（含实测规则）

以下每一条都有对应批次页 + ref，且当时与 Edge 数值逐项对齐过。

### 3.1 级联 / 选择器 / 语法
- 选择器族、`:has()`、`@layer`、`@scope`、`!important` 与 ID 优先级（b19 核查为误报）。
- **属性选择器大小写**：`[TYPE="x" i]` 合法；曾因为 `[a="v" i]` 里那个空格把整条 UA 规则判废（b172）。
- **`style` 属性必须占最高特异度**（否则 class 压过行内，b30）。
- **简写切分必须括号感知**（`calc()`/`url()` 内的空格不能切）；简写内重复分量（如 `columns: 92px 8px`）→ **整条声明作废**（b166 s7）。
- **CSS 词法不吞空白**：`@counter-style` 块解析前必须显式 `SkipWhitespaceAndComments()`，且 `ConsumeRawBlock` 在 `{` 已被消费时会**丢掉块首 token**（用自写的 `ConsumeBlockText()` 逐 token 收）。
- `all` 简写支持 CSS-wide keyword（initial/inherit/unset/revert）。
- 初值缺省两条套路已对拍（b165 initial-values survey）。

### 3.2 长度单位 / calc
- `q` 等绝对单位、绝对字号关键字表；`ch/ex/cap/ic/lh` 在 `font-size` 里参与解析（#148）；`ic` = 水字宽。
- 动态视口单位裸值 `svh/lvh/dvh` 与 `rlh`（取根行高）。
- `calc()` 数学函数 `round()/mod()/rem/clamp()` 实测语义（b26）；**陷阱：`%` 与 `em` 在 calc 内共用参考值**时容易双双错。
- **FontUnitContext 必须在每一个 `ToPixels` 调用点包 scope**，否则 em 基准落到默认 16（b23）。`Length.ToPixels` 的首参同时充当 em 与 % 基准（b125 踩过）。
- `text-indent` 的 em 必须按**本元素字号**解析（曾传 0 导致属性整体失效）；其 calc 内百分比按包含块宽（#150）；缩进要扣进分配空间；`each-line`/`hanging` 组合语义已实测。
- 百分比 margin/padding 基准 = **父 content 宽**；`padding` 百分比垂直方向按宽度（CSS 2.1）。

### 3.3 盒模型 / 块布局
- **auto 宽块级子元素扣自身水平 margin**（body 曾溢出 16px）。
- 匿名块盒清零（margin/padding/border 归 0，`width/height` auto）。
- **块级容器 auto 高度必须含自身 block-start border+padding**（#178）：`FinishLayout` 的行游标是**内容盒相对**，内在块尺寸是 **border-box 量**，两者混用会少一截。判据：`css-standard-verify182-block-start-strut.html`（6 例逐值一致）。
- **clearance 锚点换算（与 #178 同源）**：浮动排除区按**内容缘**锚定（`contentBlockStart = ContainerBfcOffset + border + padding`），被 `clear` 推下的子元素回报位置因此多带一个 strut；折进游标前必须减掉，并且**与当前游标取 max**（否则连续两个 `clear` 兄弟互相重叠）。实现：`BlockLayoutAlgorithm.ChildBlockOffset()`。判据：`…184-clearance-strut.html`（6 例）+ `…183-block-height-matrix.html`（16 种子内容，15 例精确，inline-block 例差 0.4px）。
- margin 折叠（兄弟/父子/空盒/负值/与 clear 交互）：负 margin 取最负；`line-height:%` 固化成 px。
- `min-width > max-width` 时 **min 胜出**；`visibility:collapse` 行折叠与表格宽度优先级。
- 浮动：`float:left` 多块并排、块级盒与浮动避让、BFC 包含浮动子（浮动+文本不能判成纯 IFC 根）、负水平 margin 不得被 `Math.Max(0)` 夹平。
- `aspect-ratio`：参与块尺寸、给定高反推宽、`min/max-height` 下反向收缩 auto 宽、在 flex/grid 项内生效。
- `position:relative` 已落地（施加点在 `AuroraFragmentConverter` 末尾；**atomic inline 重新居中会抹掉偏移**）。
- `position:sticky`、`inset` 逻辑属性（`inset-inline` 按 direction 翻转）。
- `box-sizing:border-box` 在 flex 项、grid 项 stretch、替换元素上的一致性（替换元素**不认** box-sizing）。

### 3.4 行内 / 文本
- 行盒：`line-height:normal` **按设备像素量化**（dpr≠1 才显现，#87）；`line-height` 小于字体盒时行盒仍等于 line-height（负 half-leading，#162）；`vertical-align` 的行盒增长、百分比/文本基线、`middle`、`text-top/bottom`、`sub/super`。
- inline-block 基线：`overflow:visible` 且有行盒 → 末行基线，否则底边（#141）。
- 原子内联（图片）按基线定位并撑行盒（#163）。
- 换行：UAX#14 行首/行尾禁则实测表、`word-break: keep-all` 语义、`overflow-wrap: anywhere`、`break-spaces`、min-content 断行。
- `white-space: pre` 下 `tab-size` 制表位；`pre-wrap` 行尾空格悬挂。
- `text-align: justify`：行尾空格不计配额也不分配；强制换行的行**不**两端对齐；`text-align-last`。**已知缺口 #94：NBSP 应作为扩展点参与分配（需要切 run）**。
- `text-indent`（含 hanging/each-line/百分比/calc）。
- `text-transform`（含 small-caps 合成，比例 `clamp(x/c, 0.65, 0.8)`）、`letter-spacing`（末字符也加距）、`word-spacing`（百分比）。
- `text-decoration` 家族：线型/颜色/粗细/偏移/多片段/传播、`text-underline-position: under|alphabetic`、`text-decoration-skip-ink`、垂直书写下的装饰线、`wavy`。
- `text-overflow`：`ellipsis`（RTL 时省略号居左）、**自定义 `<string>`**（b128）。
- `text-shadow` 多重（**第一个画在最上面**）、`text-emphasis`（见下）。
- `text-emphasis`：标记字形表 + **ink 恒为文本 0.2em**（用 `FontSizeFactor=0.25` 的 CJK 强调字体画，且必须校验 `typeface.FamilyName == 请求族名`，因为 `SKFontManager.MatchFamily` 会**静默返回替代字体**）；**行盒增长已实现（#88）**：预留 = `0.5×文本字号的标记字体 em 盒 + separation(clamp(fs/10,1,2))`，增长 `max(0, 该盒 − half-leading)`，over/under 分侧。判据 `…181-text-emphasis-line-box.html`（25 组，差 ≤1.5px）。
- `::first-line`、`::first-letter`（drop-cap 浮动仍待办）、`::marker { content }` 覆盖。
-  bidi：L2 逐层回环、RTL start 位移、`ComputeBidiFlags` 接线。

### 3.5 列表与计数（本轮重点，规则最硬）
- `list-style-position: inside/outside`、`::marker` 样式、`list-style` 简写（含 `url()` 与带引号 `<string>`；空 `url()` → none）。
- `ol/ul` 的 `type` 表现属性 → 必须做成 **UA 规则且特异度 (0,1,0)** 才压得过 UA 表；`start` / `reversed` / `li value=` 编号。
- **两套默认值不要混**：marker 序号按 HTML 语义（`start` 默认 1；`reversed` 无 `start` 默认 = 条目数；`value=` 重锚），而 CSS `counter(list-item)` **只认属性驱动**（`reversed` 无 `start` → 0，且忽略 `value=`）。
- **计数作用域（CSS GCP §4.2）**：每元素一个帧栈；`counter-reset` 开子树作用域；对不存在的名字做 `counter-increment`/`counter-set` 会**创建在父帧**（Edge 实测 `A[5]` / 兄弟 `A[5]` / 另一个列表 `B[0]`）；`counters()` 由外向内拼接。
- **@counter-style 已实现**（`Acrux.Core/Css/CounterStyle.cs` + 注册表 `CounterStyleRegistry`）：六种 system（cyclic/numeric/alphabetic/symbolic/additive/fixed）+ `extends`/`fallback` 链 + 描述符 `additive-symbols/negative/pad/range/prefix/suffix/system`。
  - **表示(representation) 与 后缀(suffix) 是两层**：`counter()` 只用表示（不加前后缀）但**会应用 `pad`**；marker 才加后缀。
  - Edge 实测：**单个描述符值非法只作废该描述符**；`system` 初值 = `cyclic`；`pad` 缺省填充字符 = **空串**；`numeric` 至少 2 个符号；`suffix`/`prefix` 只取一个 symbol；未知自定义名 → 按 decimal；`extends` 唯一合法语法是 `extends <name>`。
  - 预设样式表已按规范校正（日文 additive ±9999 + `cjk-decimal` 兜底、韩文长手"紧邻标记的 1 全省略"、hebrew 用 geresh U+05F3、armenian 1..9999、`leading-zero` 负值不补零）。
  - `counter(x, "<带引号样式>")` → **整条 content 作废**（不是回退）。
- marker 盒宽：**盒 = 标签 + 后缀空格**（`、`、引号串、自定义 `content` 不加空格）；外标记盒右缘贴内容缘；**符号型（disc/circle/square）inside 盒宽 = 1.4×标记字号**。
- ⚠️ 仓库里有**两个 `ListMarker.cs`**（`Acrux.Core/Layout/List/` 与另一处），别改错文件。

### 3.6 背景 / 边框 / 圆角 / 遮罩
- 多重背景、`background-blend-mode`、`background-origin/clip`（含 `text` 裁剪渐变）、`background-position` 的 calc、`background-repeat: space/round`、`background-size` 与 position 配合。
- ⚠️ **已知缺口 #127：多层 background 的逐层 position/size/repeat** —— 数据模型目前只有标量字段（`BackgroundImage` 是 List，其余全单值），多层会退化。渲染侧已有 `FillLayer` 链类型可用。
- 边框：逻辑属性简写、`thin/medium/thick` 与缺省 medium、`border:0` 误判、`style:none` 必须清零宽度、**折叠边框单元格半边盒模型 + 表格块轴盒尺寸**。
- **边框落设备像素栅格实测规则：`max(1, floor(w*dpr))/dpr`**（b149/b150，dpr≠1 才显现）。
- 圆角：5 处半径消费者必须共用 `ResolveRadii`；内半径**同心收缩**；`IsRenderable` 用 Y 半径比高度；绘制端解码百分比/椭圆半径；四色圆角边框按 45° 楔形填色；outline 只对真实圆角加 offset。
- **背景填充矩形与圆角解析框必须同一个 rect**（`op.Rect` 曾用 paddingRect 而圆角按 borderRect 解析 → 非对称百分比圆角出白缝，#98）。
- 渐变：linear 渐变线公式（曾有 out 参数翻转 bug）、radial 圆心/半径/硬色标、conic 两位色标与 `from` 角度；**渐变必须走平铺几何**（不能整块刷）；`border-image` 渐变源；`mask-image` 渐变遮罩、`mask-mode: luminance` 系数、`mask-composite`（Skia 无 subtract → 逐像素算 alpha）。
- `clip-path`（裁剪背景但不裁元素自身文本；几何盒不能写进形状函数括号）、`box-shadow` 多重/inset、`outline`（含 dashed/dotted、offset、**内联元素的 outline 只能画在片段函数里**）。
- `filter` 链：`CreateCompose` 先算 inner 所以**顺序要反着接**；无括号 `filter` 整条作废；`opacity()`。

### 3.7 Flex / Grid / 多列
- Flex：`gap`（含百分比按容器尺寸、轴映射）、`align-items: baseline`、`align-content: flex-start`（曾被当 stretch）、`flex-wrap`、自动边距与自动最小尺寸、三值简写 `flex:0 0 100px`（裸 0 不能被当 basis）、`min()/max()/clamp()` 宽与百分比基准、`grow/shrink` 分配必须扣 item 的 border+padding、content-box 容器不得双扣自身 border/padding。
- Grid：`auto-fit/auto-fill`（含空轨塌缩、gap 计数）、`template-areas`、`minmax`、`min-width:auto` = 最小内容、`justify-self/place-items`（用 content 盒算偏移会溢出）、项 stretch 下扣 margin、`grid-row/column` 无斜杠简写、轨道 `calc()/min()/max()/clamp()`、容器 `width:max-content/min-content` 收缩到轨道总宽、`justify-content: space-between/around/evenly`。**待办 #102 subgrid**。
- 多列（本轮大改，规则见 §6.2）：`columns` 简写、`column-gap` 真实参与列宽（曾用硬编码 16）、`column-span: all`、定高 + `column-fill`、平衡算法（二分找最小可放高度）、列文字绘制（布局对但 paint 层偏移丢失过）。

### 3.8 表格
- `display: table` 家族、`width` 表现属性、`colspan/rowspan` 列宽分配、`table-layout: fixed`、`border-spacing` 双值、`empty-cells: hide`、单元格 `vertical-align`（top/middle/bottom/baseline）、折叠边框、UA 边距 quirk。
- 空 `td` 在 Chrome 是 **0×0**；单元格不得被 `vertical-align` 二次应用撑大行盒。
- **待办 #153**：匿名 row/table 包裹与静态度量（b140/b141 的堆叠与列排布与 Chrome 不同）。
- **待办 #180**：行高未取 `max(指定高, 行盒)` → 表格整体偏矮（b16 数据见 §5）。

### 3.9 图像 / 替换元素 / 表单
- `object-fit`（含 `scale-down`、`cover` 裁剪填充色 (255,128,128) 这类像素判据）、`object-position` 百分比居中、`content: url()` 生成图像、`list-style-image`。
- 表单控件默认尺寸实测表（select/input/button）、`select` 列表框不画下拉箭头、`accent-color`、`caret-color`、`buttonface` 等**系统色未实现**（曾致 button 全黑，已修 outset/底色）。
- **待办 #164**：UA 表替换元素 `display` 与 Chrome 不一致（inline vs inline-block）+ `middle` 对齐 0.45px。

### 3.10 颜色 / 字体 / 其他
- 现代颜色语法（hex 缩写、`rgb(a)`/`hsl(a)`、Color 4 空间 `lab/lch/oklab/…`，**lab 是 D50**）、`currentColor`、颜色正则必须锚定、`canvas getImageData` 取色法对拍。
- 字体：字族回退（含 `font: 20px Arial` 简写丢未加引号字族名的历史坑）、通用字族度量（monospace 的 `0` advance 0.597em）、**谚文/假名/emoji 按码位回退**（绘制端 + 度量端两处；含代理对回归）、`font-variant`/small-caps。
- **待办 #89/#90**：`font-size-adjust`/`font-optical-sizing` 未真正参与度量；`font-variant-numeric`（OpenType 特性 + 上/下标合成）。
- 滚动/交互相关：`scroll-behavior`、`overscroll`、`overflow` 两值简写、`resize`、`cursor`、`pointer-events`、`will-change`、`contain`、`content-visibility`、`column-rule` 绘制。
- 动画/过渡：`transition`、`@keyframes`、离屏动画 park 与 idle 双窗口门禁（判定要映射**祖先 transform**；parked 仍须 250ms 采样；动画结束必须同趟重跑 style+sample）。

---

## 4. 已知缺口 / 待完善清单（带实测数据，接手就从这里挑）

| # | 缺口 | Edge 实测 / 判据 |
|---|---|---|
| 97 | 绝对定位包含块：现在误用**直接父元素**，缺定位祖先链与 ICB 回退。设计已定（按 CB 几何沿 `ConstraintSpace` 下传） | b129 / b42 |
| 94 | justify 时 NBSP 应作为扩展点参与分配（需把"折断机会"与"扩展机会"分离并切 run） | b28/b30 已定性 |
| 127 | 多层 background 逐层 position/size/repeat（数据模型只有标量） | b78 |
| 131 | `writing-mode` 竖排布局 | 未开工 |
| 153 | 表格：匿名 row/table 包裹与静态度量 | b140/b141（cells 尺寸已对，堆叠/列排布未对） |
| 160 | 泰文/天城文能画但度量差 3–5px | b147 s3/s8 |
| 164 | UA 替换元素 `display` 与 Chrome 不一致 + `middle` 差 0.45px | b167 |
| 174 | 符号型 marker 盒宽在 fs≥29 后比 1.4× 少 0.4–4px（台阶呈 ±0.5 设备像素抖动）。**本轮判定为不值得拟合**：16 个点凑出的公式无可解释成因，疑为 Chrome 字形度量取整。保留固定 1.4×（≤28px 精确，96px 偏 3.2px） | b178 探针含 12/14/16/18/20/24/26/28/29/30/31/32/40/48/49/50/51/52/60/68/69/70/71/72/88/89/90/91/96/108/110 全表 |
| 88 余项 | `::first-letter` 的 drop-cap 浮动 | b51 |
| 装饰线 | 块级装饰线 thickness/offset 在部分场景仍待办 | b28/b30 记录 |
| **179** | 内在尺寸关键字算错：`width:min-content/max-content` 与 flex 内在宽度 | b48（Edge 37.6/148/28 vs 我们 56/56/36）、b47、b115 |
| **180** | 表格行高未取 `max(指定高, 行盒)` | b16（Edge 63.2/65.6/83.2/80 vs 我们 58/47.2/64.4/58.8；宽度多数已一致） |
| **181** | multicol 作为容器**首个**子元素时丢失父级 block-start 偏移（body margin 20px 被吞） | 探针 `snapshots/out/_probe_first_offset.html`：Edge m1 top=20，我们 0。试过"只回报 `ForcedBfcBlockOffset`"→ 变成 60 且不推进游标，更糟，**已回退**。根因位置：`ColumnLayoutAlgorithm.cs:410` 与 `:653` 的 `BfcBlockOffsetValue`/`BfcLineOffset` 与父块的交接语义 |
| 89/90 | font-size-adjust / font-variant-numeric | 未开工 |
| 102 | grid subgrid | 未开工 |

---

## 5. 实现层"陷阱地图"（跨会话反复踩）

1. **两套坐标锚点混用**（本轮 #178 + clearance 两处同源）：块布局的行游标是**内容盒相对**，而内在尺寸/BFC 交接是 **border-box 量**。任何"容器高度莫名少/多一截 border+padding"先查这里。判据三件套必须一起跑：b182 + b183 + b184。
2. **多列（`ColumnLayoutAlgorithm`）实测语义**：
   - 流线程必须按**列宽**布局（重跑容器块算法会让容器指定宽渗进流里 → 列叠列）。
   - 流程末尾的块边距**只在"流程确实结束于最后一个 fragmentainer"时**计入盒高（单列 multicol 因此是 72 而不是 56）；spanner 之前的组**不**计尾边距。
   - 列流内相邻兄弟的块 margin 必须**折叠**（不是相加）→ `CollapseBlockMargins`。
   - "续列时把该块 block-start margin 重新计入量测"这条规则**只适用于带 spanner 的组**，普通路径套上会把 s1/s9 撑错。
   - 平衡 = 二分找"能放下的最小高度"；`BfcLineOffset` 必须回填，否则子盒丢祖先 padding/margin 位移。
   - spanner 是**真实元素**：转换器会替它加 border/padding，布局侧再叠一次就会右下各偏一个 strut（曾压住下一组首行）。
3. **绘制层**：内联装饰（outline/背景/边框）只能画在**片段函数**里，且换行内联要按词合并成整行碎片；`box-decoration-break: clone` 行首占预算、行尾可溢出；`overflow` 裁剪真正发生在 `PaintLayerPainter/Clipper`，`VisitElement` 只跑根元素。
4. **`SKBitmap.FromImage` 是 BGRA 序**；`SKFontManager.MatchFamily` 会静默给替代字体（必须回验 `FamilyName`）。
5. **`IsNullOrWhiteSpace` 包含 U+00A0** —— 判"空文本"时 NBSP 会被误判为空。
6. **正则里 `rotate3d` 必须排在 `rotate` 之前**；`transform` 多函数列表重复包裹 transform-origin 会让元素飞出画面；缺 `case` 的 3D 函数会被**静默丢弃**。
7. **行内 `<a>` 曾点不动**（Phase 1b 命中缺陷）；根滚动归宿主。
8. 用行切片脚本改文件前**先打印删除区间**（本仓曾因此误删 `LayoutEngine` 三个方法且不在 HEAD 里，只能照调用点重写）。

---

## 6. 换到新机器后必须重新标定的东西

| 变量 | 影响 | 处理 |
|---|---|---|
| **Edge 的 `devicePixelRatio`** | `line-height:normal`、边框宽度都按设备像素量化 → 数值直接变 | 探针里回传 `devicePixelRatio`；本机是 **1.25**。若新机器不同，**所有 dpr 相关 ref 与 Edge 数值都要重标** |
| **已安装字体** | 度量（ascent/descent/advance）与字形回退决定像素；本机依赖 `Segoe UI / Arial / MS Gothic`（`MS Mincho/Yu Mincho/SimSun` 未装，`text-emphasis` 因此落到 MS Gothic） | 新机器先跑一次 `--dumplayout` 对比本文 §3 的数值；字体不同则**像素 ref 大面积失真属正常**，必须逐批用 Edge 数值重烤，不能直接信任旧 ref |
| 快照 dpr | `--snapshot`/`--dumplayout` 的 dpi 是**显式参数**，与 OS 缩放无关 | ref 的像素尺寸 = CSS 尺寸 × 该 dpi；历史 ref 混用 **1.0 / 1.25 / 1.5 / 2.0**（文件名里带 `dpi1`/`dpi1-5` 的就是字面意思）。`marker_gate.py` 已按 1.25→1.0→2.0 探测，遇到 1.5 类 ref 需手工指定 |
| 盘符/路径 | 脚本里写的是相对路径

---

## 7. 建议的接手流程（每轮任务）

1. `dotnet build` → 确认 0 错误（警告 400+ 是既有状态，别管）。
2. 跑门禁：`python snapshots/marker_gate.py <本轮相关批次号>`（或 `--all` 做全量，约 10 分钟）。
3. 对**非 0** 的批次：
   - 先看 `diff_bands.py` 判断是整体位移还是局部；
   - 用 `--dumplayout`（**带 1.25**）取数值，用 Edge `evaluate_script` 取同一组数值；
   - 一致 → ref 过期，`rebake_from_gate.py` 重烤（旧图自动留 `old-*.png`）；
   - 不一致 → 是真缺陷，修完再回到第 2 步。
4. 新写测试页：`snapshots/css-standard-verify<N>-<topic>.html`，页面里给每个被测盒 `id`（这样 `--computed` 能打印），并在注释里写"为什么这样测/曾被误判过什么"。
5. 出 ref：`--snapshot <page> snapshots/out/ref-b<N>-<topic>.png <cssW> <cssH> 1.25`，肉眼 `Read` 图片确认，再跑一次 `marker_gate.py N` 确认 0.000%。
6. `dotnet run --project Acrux.SmokeTest` 必须 `ALL SMOKE TESTS PASSED`。
7. **不动 git**（不 stage/commit/删除）。

---

## 8. 关键文件索引（改 CSS 行为时先看这里）

| 区域 | 文件 |
|---|---|
| 属性应用/解析 | `Acrux.Core/Css/Resolver/CssPropertyApplier.cs`（含 `ParseListStyle*`、`IsCounterStyleName`、`UnescapeCssString`） |
| 简写展开 | `Acrux.Core/Css/ShorthandExpander.cs`（**切分必须括号感知**） |
| 词法/语法 | `Acrux.Core/Css/Tokenizer/CssParserImpl.cs`（`ConsumeCounterStyleRule` / `ConsumeBlockText`） |
| 级联 | `Acrux.Core/Css/Resolver/CascadeResolver.cs`（继承与拷贝两份字段清单，**新增属性要同时加两处** + `ComputedStyle.Clone()`） |
| 计数样式 | `Acrux.Core/Css/CounterStyle.cs`（解析 + 六种 system + 注册表） |
| 计算样式模型 | `Acrux.Core/Dom/ComputedStyle.cs`（字段 + `Clone()`） |
| 块布局 | `Acrux.Core/Layout/BlockLayoutAlgorithm.cs`（游标/clearance/margin 折叠/内在尺寸） |
| 行内布局 | `Acrux.Core/Layout/InlineLayoutAlgorithm.cs`（`AlignLineBoxes` = 行盒增长唯一施加点）、`Layout/Inline/*` |
| 多列 | `Acrux.Core/Layout/ColumnLayoutAlgorithm.cs` |
| 列表标记 | `Acrux.Core/Layout/List/ListMarker.cs`、`ListMarkerFormatter.cs`、`ListItemNumbering.cs`（**注意仓库里有两个 ListMarker.cs**） |
| 计数作用域 | `Acrux.Core/Layout/CounterScope.cs`、`LayoutEngine.cs`（`DecodeCssContent` 的 counter/counters 分支） |
| 片段转换 | `Acrux.Core/Layout/AuroraFragmentConverter.cs`（relative 偏移、行盒 BlockSize） |
| 绘制 | `Acrux.Rendering/PaintVisitor.cs`、`PaintOps.cs`、`BoxPainterBase.cs`（`FillLayer`）、`BackgroundImageGeometry.cs` |
| 无头通道 | `Acrux/SnapshotCli.cs`（参数签名）、`Acrux/Program.cs`（frameshot/localshot/interact/tabtest/proctest/idletest） |

---

## 9. 本轮（b166/b174–b184）刚做完的事，接手时可直接当基线

- `@counter-style` 全量实现 + `counter()/counters()` 样式实参 + 计数作用域栈 + 列表编号两套默认值（b174/b176/b177 及 a–d 子页）。
- 多列 fragmentainer 块边距语义、列流兄弟 margin 折叠、spanner 组量测规则、spanner 盒重复 strut 修复（b166/b179/b39）。
- `text-emphasis` 标记参与行盒增长（b181，#88 关闭）。
- **#178 块起始 strut** 与 **clearance 锚点换算**（b182/b183/b184）。
- 门禁脚本修 bug：原先按**前缀**匹配 ref，会把 `ref-b22.png` 配到 b2 页面、报出 36% 的假回归；现在按完整批次号配对，并支持 dpr 探测与"首个 0.000% 即停"。
- 全量 161 张 ref 已按最终二进制重烤（旧图保留为 `old-*.png`），门禁全绿；`Acrux.SmokeTest` PASSED。
- 新登记的缺陷：#179（内在宽度关键字）、#180（表格行高）、#181（multicol 首子元素偏移）。
