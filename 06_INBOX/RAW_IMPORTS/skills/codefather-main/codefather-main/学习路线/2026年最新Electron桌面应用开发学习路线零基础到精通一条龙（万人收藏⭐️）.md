# 2026年最新Electron桌面应用开发学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Electron 求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1991432457391439874)



## 开篇介绍

Electron 是一个使用 JavaScript、HTML 和 CSS 构建跨平台桌面应用的框架，由 GitHub 开发并于 2013 年开源。Electron 将 Chromium 和 Node.js 嵌入到一个二进制文件中，让你可以使用 Web 技术开发桌面应用，一套代码可以运行在 Windows、macOS 和 Linux 上。

Electron 被广泛应用于各种桌面应用开发，从代码编辑器到通讯工具、从音乐播放器到开发工具。知名的 Electron 应用包括 VS Code、Atom、Slack、Discord、Figma、Notion、Postman 等，鱼皮团队的 [剪切助手](https://jianqiezhushou.com/) 也是用 Electron 开发的，这些应用都证明了 Electron 的强大和可靠性。

**为什么要学 Electron？**

使用 Web 技术开发桌面应用具有巨大的优势：开发效率高（复用 Web 技术栈）、跨平台（一套代码三个平台）、生态丰富（可以使用 npm 上的所有包）、界面美观（使用 CSS 自由定制）。对于前端工程师来说，学习 Electron 可以让你进入桌面应用开发领域，拓展职业发展空间。

Electron 的核心架构是多进程模型：主进程（Main Process）负责管理窗口和应用生命周期，渲染进程（Renderer Process）负责渲染页面。主进程和渲染进程通过 IPC（进程间通信）进行通信。理解这个架构是学习 Electron 的关键。

![](https://pic.yupi.icu/1/9a864c1d-c26f-46a3-9ead-a604ca9abb9e.png)

在 AI 时代，Electron 也可以开发 AI 桌面应用，比如在 Electron 应用中集成大语言模型、图像识别等 AI 能力。



### 学习前提

注意，如果你刚开始学前端，一定不要直接从 Electron 开始！

学习 Electron 建议先掌握：

1. JavaScript/TypeScript：熟练使用 ES6+ 语法【必学】。关于 JavaScript 的详细学习，可以查看 [JavaScript 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990753900055015426)；关于 TypeScript 的详细学习，可以查看 [TypeScript 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990753941477961730)
2. HTML/CSS：熟练使用前端技术【必学】。关于前端开发的详细学习，可以查看 [前端开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1789190394078011393)
3. Node.js：理解 Node.js 基础【建议】。关于 Node.js 的详细学习，可以查看 [Node.js 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754313357537282)
4. 前端框架：React、Vue 等【建议】。关于 React 的详细学习，可以查看 [React 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754184068116481)；关于 Vue 的详细学习，可以查看 [Vue.js 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754217060511746)



### 学习路线图

![Electron 桌面应用开发学习路线思维导图](https://pic.yupi.icu/roadmap/electron-desktop-development-roadmap.png)

### 就业方向

学完 Electron 后，可以帮助你从事下列岗位：

1. Electron 开发工程师：专注于桌面应用开发
2. 全栈工程师：前端 + 桌面端全栈开发
3. 客户端开发工程师：开发跨平台桌面应用
4. 工具开发工程师：开发开发者工具、效率工具



## 整体学习建议

1）Electron 使用 Web 技术开发，一定要先掌握 HTML、CSS、JavaScript 等前端技术。如果会 React 或 Vue，开发 Electron 会更轻松。

2）理解进程模型：Electron 的多进程模型是核心概念，要理解主进程和渲染进程的区别和作用。

3）Electron 的官方文档非常详细，中文文档质量也很高。遇到问题时，优先查阅官方文档。

4）Electron 的学习一定要结合实际项目。可以先开发一些简单的工具，如笔记本、待办事项等，熟悉 Electron 的开发流程。

5）学习 Electron 时可以用 AI 工具辅助开发，推荐到鱼皮的 [AI 资源大全](https://ai.codefather.cn/) 中捞捞 AI 工具。

![AI资源大全网站](https://pic.yupi.icu/1/AI%E8%B5%84%E6%BA%90%E5%A4%A7%E5%85%A8%E7%BD%91%E7%AB%99.png)



## 阶段 1：Electron 基础（10-20 天，仅供参考）

### 学习目标

理解 Electron 的核心概念，掌握 Electron 项目的创建和基本使用。



### 知识点

**Electron 简介【必学】：**

- Electron 的特点和优势
- Electron 的应用场景
- Chromium 和 Node.js 的作用

**进程模型【必学，核心重点】：**

- 主进程（Main Process）
- 渲染进程（Renderer Process）
- 预加载脚本（Preload Script）
- 进程的职责划分

**创建项目【必学】：**

- 项目初始化
- package.json 配置
- 主进程入口（main.js）
- 创建窗口（BrowserWindow）

**开发环境【必学】：**

- 开发工具（VS Code）
- 热重载（electron-reload）
- 调试工具（DevTools）



### 学习建议

1）Electron 的进程模型是核心概念，主进程类似于后端，负责管理窗口和系统交互；渲染进程类似于前端，负责渲染页面。一个应用有一个主进程，可以有多个渲染进程。

![](https://pic.yupi.icu/1/1*xT_g0v-V-Gt8-o_Bij7nCg.png)

2）预加载脚本是主进程和渲染进程之间的桥梁，可以在渲染进程中暴露主进程的 API。预加载脚本是 Electron 安全模型的重要部分。

3）BrowserWindow 是 Electron 创建窗口的核心 API，可以创建各种类型的窗口（如主窗口、对话框、无边框窗口等）。



### 学习资源

- ⭐ [Electron 官方文档](https://www.electronjs.org/zh/docs/latest/)：最权威的学习资料（中文）
- [尚硅谷 Electron 教程](https://www.bilibili.com/video/BV1sE421N7M5)：1 小时快速上手



## 阶段 2：进程间通信（IPC）（10-20 天，仅供参考）

IPC 是 Electron 的核心功能，主进程和渲染进程通过 IPC 通信。渲染进程调用主进程的功能（如文件操作、系统对话框）需要通过 IPC。



### 学习目标

掌握 Electron 的进程间通信，能够实现主进程和渲染进程的数据交互。



### 知识点

**IPC 基础【必学，核心重点】：**

- IPC 的概念
- ipcMain 和 ipcRenderer
- 单向通信和双向通信

**渲染进程到主进程【必学】：**

- ipcRenderer.send()（单向）
- ipcRenderer.invoke()（双向）
- ipcMain.on()
- ipcMain.handle()

**主进程到渲染进程【必学】：**

- webContents.send()
- 主进程主动推送消息

**上下文隔离【必学】：**

- contextBridge
- 暴露安全的 API



### 学习重点

1）invoke/handle 是推荐的双向通信方式，比 send/on 更简洁。invoke 返回 Promise，可以等待主进程的响应。

2）出于安全考虑，渲染进程不能直接访问 Node.js API。需要在预加载脚本中使用 contextBridge 暴露安全的 API。

3）理解上下文隔离和 contextBridge 是 Electron 安全开发的关键。



### 学习资源

- ⭐ [Electron IPC 官方文档](https://electronjs.org/zh/docs/latest/tutorial/ipc)：官方文档
- [Electron 主进程与渲染进程通信](https://comate.baidu.com/zh/page/292m186weoh)：全解析
- [Electron IPC 通信实战](https://developer.aliyun.com/article/1246779)：实现方案



## 阶段 3：原生能力（7-20 天，仅供参考）


Electron 可以访问 Node.js 的所有 API，可以进行文件操作、网络请求、调用系统功能等。这是 Electron 相比 Web 应用的最大优势。



### 学习目标


本阶段的目标是掌握 Electron 的原生能力，能够调用系统 API。



### 知识点

**系统对话框【必学】：**

- dialog（打开文件、保存文件、消息框）
- 原生菜单（Menu）
- 托盘图标（Tray）

**文件系统【必学】：**

- Node.js fs 模块
- 文件读写
- 文件监听

**系统通知【建议学】：**

- Notification
- 系统通知的权限

**其他原生能力【建议学】：**

- clipboard（剪贴板）
- globalShortcut（全局快捷键）
- screen（屏幕信息）
- powerMonitor（电源监控）【可不学】



### 学习重点

1）系统对话框可以提供原生的用户体验，如打开文件对话框、保存文件对话框等。要熟练使用 dialog API。

2）托盘图标可以让应用最小化到系统托盘，提供更好的用户体验。很多桌面应用都有托盘图标。



### 学习资源

- [Electron API 文档](https://www.electronjs.org/zh/docs/latest/api/app)：完整 API 参考



## 阶段 4：打包和发布（7-15 天，仅供参考）

### 学习目标

打包和发布是将 Electron 应用分发给用户的重要环节，需要掌握应用签名、自动更新、多平台打包等技能。



### 知识点

**应用打包【必学】：**

- electron-builder
- electron-forge
- 打包配置
- 应用图标和元信息

**自动更新【建议学】：**

- autoUpdater
- electron-updater
- 更新服务器搭建

**代码签名【建议学】：**

- Windows 代码签名
- macOS 代码签名和公证
- 证书申请

**分发渠道【建议学】：**

- Windows（官网、Microsoft Store）
- macOS（官网、Mac App Store）
- Linux（官网、Snap、Flatpak）



### 学习重点

1）electron-builder 是最流行的 Electron 打包工具，可以打包为 Windows exe、macOS dmg/pkg、Linux deb/rpm 等格式。

![](https://pic.yupi.icu/1/maxresdefault-20251202122232433.jpg)

2）自动更新可以让用户自动获取新版本，提升用户体验。electron-updater 是流行的自动更新库。

3）代码签名可以让应用获得系统的信任，避免安全警告。但代码签名需要购买证书，个人开发者可以跳过。鱼皮当初折腾过这一块，还是挺麻烦的。



### 学习资源

- [electron-builder 文档](https://www.electron.build/)：打包工具
- [electron-updater 文档](https://www.electron.build/auto-update)：自动更新



## 阶段 5：项目实战（20-30 天，仅供参考）

### 学习目标

通过实际项目巩固所学知识，积累 Electron 项目经验。



### 学习建议

1）从简单工具开始：如笔记本、待办事项、计算器等，熟悉 Electron 的完整开发流程。

2）结合前端框架：使用 React、Vue 等框架开发 Electron 应用，可以大大提高开发效率。Vite + React/Vue + Electron 是流行的技术栈。

3）关注性能：Electron 应用的启动速度和内存占用是常见问题，要注意优化。

4）打包发布：完成项目后，打包成可执行文件，发布到官网或应用商店。



### 项目推荐

**入门级项目：**

- Markdown 笔记本
- 待办事项应用
- 音乐播放器
- 图片浏览器

**进阶级项目：**

- 代码编辑器
- 聊天应用（IM）
- 视频播放器
- 文件管理器
- 系统监控工具

**优质开源项目：**

- ⭐ [Electron](https://github.com/electron/electron)：119k+ stars，Electron 官方仓库及示例
- [VS Code](https://github.com/microsoft/vscode)：165k+ stars，使用 Electron 开发的代码编辑器（难度较大，仅供参考）
- [Electron React Boilerplate](https://github.com/electron-react-boilerplate/electron-react-boilerplate)：23k+ stars，Electron + React 脚手架



### 学习资源

- [Electron 示例代码](https://www.electronjs.org/zh/docs/latest/tutorial/examples)：官方示例
- [尚硅谷 Electron 教程](https://www.bilibili.com/video/BV1sE421N7M5)：1 小时快速上手



## 阶段 6：求职备战

### 学习目标

熟练掌握 Electron 常见面试题，准备好简历和项目经历，顺利通过面试。



### 学习建议

1）准备项目：如果公司岗位要求明确提到了 Electron 技术，那么简历上必须要有 Electron 桌面应用项目；会桌面端开发对前端工程师来说也是很加分的。面试时要能够清楚地介绍项目的技术架构、遇到的问题和解决方案等。

2）准备简历：建议使用 [老鱼简历](https://www.laoyujianli.com/) 制作简历，关于如何写好简历，推荐学习鱼皮的 [保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)。

![老鱼简历网站简历模板大全](https://pic.yupi.icu/1/%E8%80%81%E9%B1%BC%E7%AE%80%E5%8E%86%E7%BD%91%E7%AB%99%E7%AE%80%E5%8E%86%E6%A8%A1%E6%9D%BF%E5%A4%A7%E5%85%A8.png)

3）多刷面试题：Electron 的面试题主要包括进程模型、IPC 通信、原生能力、打包发布等。建议使用 [面试鸭](https://www.mianshiya.com/) 刷题。

![面试鸭刷题神器前端八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8%E5%89%8D%E7%AB%AF%E5%85%AB%E8%82%A1%E6%96%87.png)

更多求职干货：[编程导航求职干货分享](https://www.codefather.cn/learn?category=76&type=2)



### 经典面试题

**基础概念：**

1. Electron 是什么？有什么特点？
2. Electron 的进程模型是怎样的？
3. 主进程和渲染进程有什么区别？
4. 什么是预加载脚本？

**IPC 通信：**

1. Electron 的 IPC 通信是什么？
2. ipcRenderer.send 和 ipcRenderer.invoke 有什么区别？
3. 如何从主进程向渲染进程发送消息？
4. 什么是 contextBridge？为什么需要它？

**原生能力：**

1. Electron 如何实现系统对话框？
2. Electron 如何访问文件系统？
3. Electron 如何实现托盘图标？

**打包发布：**

1. Electron 如何打包？
2. electron-builder 是什么？
3. 如何实现自动更新？



### 面试题库

- ⭐ [Electron 面试题 - 面试鸭](https://www.mianshiya.com/bank/1991432457391439874)
- [前端经典面试题合集 - 面试鸭](https://www.mianshiya.com/bank/1773175672114601986)



### 求职资源

- ⭐ [鱼皮的保姆级求职指南](https://www.codefather.cn/course/job)：从简历到面试的完整指南
- ⭐ [鱼皮的保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)：如何写出高质量简历
- [编程导航的求职干货分享](https://www.codefather.cn/learn?category=76&type=2)：求职经验和技巧
- [老鱼简历](https://www.laoyujianli.com/)：写简历工具 + 简历模板大全
- [真实面经大全](https://www.codefather.cn/job/experience)：了解真实的面试流程
- [几百场真实面试视频](https://www.codefather.cn/mockInterview)：观看他人的面试过程
- [1 对 1 模拟面试](https://ai.mianshiya.com/)：AI 模拟面试练习
- [面试题讲解视频](https://space.bilibili.com/3546383483144655)：面试鸭官方题解



## 更多资源

### 知识总结

- ⭐ [编程导航](https://www.codefather.cn/)：学习路线、项目教程、面试题、编程资源一站式平台
- [前端开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1789190394078011393)：完整的前端学习路线
- [Node.js 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754313357537282)：完整的 Node.js 学习路线

### Electron 资源

- [Electron 官网](https://www.electronjs.org/zh/)：官方网站
- [Electron GitHub](https://github.com/electron/electron)：源码
- [Electron Fiddle](https://www.electronjs.org/fiddle)：在线编辑器
- [awesome-electron](https://github.com/sindresorhus/awesome-electron)：Electron 资源大全

### 技术博客

- [Electron 官方博客](https://www.electronjs.org/blog)：Electron 官方技术博客
- [GitHub Engineering](https://github.blog/category/engineering/)：GitHub Electron 应用
- [VS Code Blog](https://code.visualstudio.com/blogs)：VS Code Electron 实践
- [Slack Engineering](https://slack.engineering/)：Slack 桌面应用



## 写在最后

Electron 是使用 Web 技术开发跨平台桌面应用的优秀选择，让前端工程师也能开发桌面应用。

学习 Electron 要先打好前端基础，理解 HTML、CSS、JavaScript。然后学习 Electron 的进程模型、IPC 通信、原生能力等核心概念。多做实战项目，体验 Electron 的完整开发流程。

希望这份学习路线能够帮助大家少走弯路，快速掌握 Electron 桌面应用开发。加油鱼友们 💪🏻！

![](https://pic.yupi.icu/1/%E5%8A%A0%E6%B2%B9%E8%A1%A8%E6%83%85%E5%8C%85.jpeg)



## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
