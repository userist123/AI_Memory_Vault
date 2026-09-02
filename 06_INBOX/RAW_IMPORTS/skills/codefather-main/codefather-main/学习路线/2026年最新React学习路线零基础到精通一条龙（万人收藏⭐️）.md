# 2026年最新React学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



React 求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1817900465338241026)



## 开篇介绍

React 是由 Facebook（现 Meta）开发并开源的 JavaScript 前端框架，于 2013 年首次发布。React 的核心思想是组件化和声明式编程，通过将界面拆分成独立、可复用的组件，让前端开发变得更加高效和可维护。React 不仅可以用于 Web 开发，还可以通过 React Native 开发移动应用，实现 “一次学习，到处编写”。

React 的核心特点是虚拟 DOM 和单向数据流。虚拟 DOM 是 React 对真实 DOM 的抽象，通过 Diff 算法最小化 DOM 操作，提升性能；单向数据流使得数据流动更加清晰可控，便于调试和维护。React 使用 JSX 语法，将 HTML 写在 JavaScript 中，虽然一开始可能不太习惯，但用过之后会发现非常方便。

![](https://pic.yupi.icu/1/image-20251202140747958.png)



**为什么要学 React？**

React 是全球最流行的前端框架之一，在国外的使用率甚至超过 Vue！很多大厂都在使用 React，掌握 React，能够让你有更多机会找到前端工作，而且 React 的薪资普遍高于 Vue。

React 的版本更新迭代非常快，截止到鱼皮写这篇文章的时候，React 的最新版本已经是 19 了。React 19 引入了服务器组件（Server Components）、Actions、use Hook 等新特性，进一步提升了开发体验和性能，建议重点学习这些新特性。

React 的生态系统非常丰富，不光有传统库，还可以依赖 JS 的很多 AI 相关的库和工具，比如 TensorFlow.js、AI SDK 等。鱼皮测下来用 AI 生成 React 前端代码的准确度也是很高的，结合 React 和 AI Vibe Coding 技术，可以快速开发出功能强大的 Web 应用。



### 学习前提

学习 React 建议先掌握：
1. HTML 和 CSS：网页的基础
2. JavaScript 基础：变量、函数、对象、数组等
3. ES6+ 语法：箭头函数、Promise、async/await、解构等【重点】

如果还不会 JavaScript，可以查看 [JavaScript 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990753900055015426)，或者查看 [前端开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1789190394078011393) 中的 JavaScript 部分。



### 学习路线图

![React 学习路线思维导图](https://pic.yupi.icu/roadmap/react-roadmap.png)



### 就业方向

学完 React 后，可以从事以下岗位：

1. 前端开发工程师：使用 React 开发 Web 应用
2. 全栈工程师：React 前端 + Node.js/Java 后端
3. 移动端开发工程师：使用 React Native 开发移动应用
4. 桌面应用开发工程师：使用 Electron + React 开发桌面应用



## 整体学习建议

1）掌握 JavaScript 是前提！

React 大量使用 ES6+ 语法（如箭头函数、解构、展开运算符），如果 JavaScript 基础不牢固，学习 React 会很吃力。

2）重点掌握 Hooks：Hooks 是现代 React 开发的标准。要重点学习 useState、useEffect、useContext、useMemo、useCallback 等常用 Hooks。

3）React 的学习要结合项目实践，边学边做。建议每学完一个知识点就动手实现一个小功能。

4）React 的官方文档写得非常好，有大量示例和交互式教程，建议多看官方文档。

5）学习 React 时可以多用 AI 工具（如 ChatGPT、Cursor、TRAE 等）辅助学习和开发。可以多看看鱼皮 [AI 资源大全](https://ai.codefather.cn/) 中的工具。

![AI资源大全网站](https://pic.yupi.icu/1/AI%E8%B5%84%E6%BA%90%E5%A4%A7%E5%85%A8%E7%BD%91%E7%AB%99.png)



## 阶段 1：React 基础（7-20 天，仅供参考）

### 学习目标

掌握 React 的基础知识，能够使用 React 开发简单的应用。



### 知识点

**React 基础概念【必学】：**

- React 的特点和优势
- 虚拟 DOM 和 Diff 算法
- JSX 语法
- 组件和 Props
- 单向数据流

**项目创建【必学】：**

- Create React App（传统方式）
- Vite（现代方式，推荐）
- 项目结构

**JSX【必学】：**

- JSX 的语法规则
- JSX 中的表达式
- 条件渲染（&&、三元运算符）
- 列表渲染（map）
- 事件处理

**组件【必学】：**

- 函数组件（推荐）
- 类组件（了解即可）
- Props 的传递和使用
- 组件的组合和嵌套



### 学习重点

1）Vite 是现代前端项目的推荐构建工具，启动速度快、热更新快。建议使用 Vite 创建 React 项目。

2）JSX 是 JavaScript 的扩展语法，看起来像 HTML 但实际是 JavaScript。要理解 JSX 会被编译成 React.createElement 函数调用。

3）React 推荐使用函数组件 + Hooks，类组件已经不推荐使用。如果看到老教程讲类组件，可以跳过，重点学习函数组件。

4）Props 是 React 组件之间通信的主要方式，要理解 Props 的单向数据流（从父组件传递到子组件）。

再次强调，这个阶段要多写代码！熟悉 JSX 语法和组件的使用。



### 学习资源

- ⭐ [React 官方文档](https://zh-hans.react.dev/)：最权威的学习资料（2023 年全面重写）
- [2025 年 React 教程](https://www.bilibili.com/video/BV1fpANeVEnS)
- [30 分钟学会 React 18 核心语法](https://www.bilibili.com/video/BV1pF411m7wV)


### 面试题库

- [React 基础面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900465338241026)



## 阶段 2：React Hooks（7-20 天，仅供参考）

React Hooks 是特殊函数，允许你使用函数组件中的状态和其他 React 功能，而无需写类。Hooks 是 React 的核心特性，也是现代 React 开发的标准。

![](https://pic.yupi.icu/1/hooks_in_react.webp)



### 学习目标

掌握 React Hooks，能够使用 Hooks 管理组件的状态和副作用。



### 知识点

**基础 Hooks【必学】：**

- useState：状态管理
- useEffect：副作用处理
- useContext：上下文

**性能优化 Hooks【必学】：**

- useMemo：缓存计算结果
- useCallback：缓存函数
- memo：缓存组件

**其他 Hooks【建议学】：**

- useRef：引用 DOM 或保存变量
- useReducer：复杂状态管理
- useLayoutEffect：同步副作用
- useImperativeHandle：自定义暴露给父组件的实例值

**自定义 Hooks【必学】：**

- 自定义 Hooks 的创建
- 逻辑复用

**React 19 新 Hooks【建议学】：**

- use：通用的资源读取 Hook
- useOptimistic：乐观更新
- useFormStatus：表单状态



### 学习重点

1）useState 和 useEffect 是最常用的 Hooks，一定要熟练掌握。useState 用于管理组件的状态，useEffect 用于处理副作用（如网络请求、订阅事件）。

2）useEffect 的依赖数组非常重要，要理解依赖数组的作用和 useEffect 的执行时机。空数组表示只在组件挂载时执行一次，不传数组表示每次渲染都执行。

3）useMemo 和 useCallback 用于性能优化，可以避免不必要的重新渲染和重新计算。但不要过度优化，只在确实有性能问题时使用。

4）自定义 Hooks 是逻辑复用的最佳实践，可以将可复用的逻辑抽取成独立的 Hook。

5）React 19 的 use Hook 是一个通用的资源读取 Hook，可以读取 Promise、Context 等资源，简化了异步数据的处理。



### 学习资源

- ⭐ [React Hooks 官方文档](https://zh-hans.react.dev/reference/react)：官方文档
- [React 19 新特性](https://zh-hans.react.dev/blog/2024/12/05/react-19)：官方博客



### 面试题库

- [React 基础面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900465338241026)
- [React 进阶面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900573026996225)



## 阶段 3：React Router（2-7 天，仅供参考）

React Router 是 React 的路由管理库，用于构建单页面应用。



### 学习目标

掌握 React Router，能够实现页面路由和导航。



### 知识点

**路由基础【必学】：**

- 路由的概念和作用
- 安装和配置 React Router
- BrowserRouter 和 HashRouter
- Route、Routes、Link
- 编程式导航（useNavigate）

**动态路由【必学】：**

- 路由参数（useParams）
- 查询参数（useSearchParams）

**嵌套路由【必学】：**

- 嵌套路由的配置
- Outlet 组件

**路由守卫【建议学】：**

- 路由拦截和权限控制



### 学习建议

1）React Router 的版本更新迭代特别快，而且不同版本间可能存在较大的变化，因此建议优先看官方文档来学习最新的版本，不要学习旧版本。

2）BrowserRouter 使用 HTML5 History API，URL 更美观；HashRouter 使用 Hash 模式，兼容性更好。**一般推荐使用 BrowserRouter。**



### 学习资源

- ⭐ [React Router 官方文档](https://reactrouter.com/)：官方文档



### 面试题库

- [React Router 面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900635140444161)



## 阶段 4：状态管理（3-7 天，仅供参考）

前端状态管理是关于如何组织、跟踪和更新前端应用中动态数据的方法和实践。包括管理用户交互、服务器响应等各种状态，确保应用程序在不同组件之间保持数据的一致性、稳定性和响应性。

说人话就是：把各个页面或组件都需要的数据集中管理起来，便于公共读写。

React 的状态管理方案有很多，可以根据项目规模选择。



### 学习目标

掌握至少一种状态管理方案，能够管理应用的全局状态。



### 知识点

**Context API【必学】：**

- Context 的创建和使用
- useContext Hook
- Context 的优缺点

**状态管理库【建议学】：**

- **Zustand**（推荐）：轻量级、简单易用
- **Redux Toolkit**：传统方案，功能强大但复杂
- **Jotai**：原子化状态管理
- **Recoil**：Facebook 出品



### 学习建议

1）Context API 是 React 内置的状态管理方案，适合中小型应用。对于简单的全局状态（如主题、用户信息），使用 Context 就足够了。

2）对于大型应用，建议使用专门的状态管理库。Zustand 据说是 2025 年最推荐的状态管理库，简单易用、体积小、性能好。不过鱼皮本人没有用过，不作评价。

3）Redux 虽然是传统的状态管理方案，但代码量较大、学习成本较高。如果不是特殊需求，建议使用 Zustand 而不是 Redux。



### 学习资源

- [Zustand 官方文档](https://zustand-demo.pmnd.rs/)：官方文档
- [Redux Toolkit 官方文档](https://redux-toolkit.js.org/)：官方文档



### 面试题库

- [React 状态管理面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900696662495234)



## 阶段 5：React 19 新特性（可选）

React 19 引入了很多新特性，建议了解。



### 知识点

**服务器组件【建议学】：**

- Server Components 的概念
- 'use client' 和 'use server'
- RSC Payload

**新特性【建议学】：**

- Actions：表单处理的新方式
- use Hook：通用资源读取
- useOptimistic：乐观更新
- useFormStatus：表单状态



### 学习重点

1）服务器组件是 React 19 的重要特性，但需要配合框架（如 Next.js）使用。如果不使用 Next.js，可以先不学习服务器组件。

2）Actions 简化了表单处理，不需要手动管理 loading 状态和错误处理。



### 学习资源

- ⭐ [React 19 发布文章](https://zh-hans.react.dev/blog/2024/12/05/react-19)：官方博客
- [React 服务器组件文档](https://zh-hans.react.dev/reference/rsc/server-components)：官方文档



## 阶段 6：项目实战（30-60 天，仅供参考）

### 学习目标

通过实际项目巩固所学知识，积累项目经验。



### 学习建议

1）从简单项目开始：先开发一些简单的项目，如待办事项、天气查询、音乐播放器等。

2）使用 UI 组件库：推荐使用 Ant Design（国内最流行）、Material-UI、Chakra UI 等 UI 组件库。

3）完整的技术栈：React + Vite + React Router + Zustand + Ant Design + Axios

4）前后端分离：结合后端 API 开发完整的前后端分离应用。

5）学习 Next.js：Next.js 是 React 的服务端渲染框架，是 React 生态的重要组成部分，建议学习。



### 项目推荐

**纯 React 练手项目：**

- 待办事项（TodoList）
- 天气查询
- Hacker News 克隆
- 计算器
- 后台管理系统
- 博客系统
- 电商网站
- 社交平台

**鱼皮原创项目（React 技术栈，强烈推荐）：**

入门项目：

1. [用户中心项目](https://www.codefather.cn/course/1790943469757837313)：React + Ant Design Pro 前后端分离项目，适合前端新手入门，系统学习完整的项目开发流程和上线方法

企业级项目（选择 2-3 个）：

1. [智能面试刷题平台](https://www.codefather.cn/course/1826803928691945473)：React + Next.js 服务端渲染网站，学习 SSR、多级缓存、Elasticsearch 搜索、Sa-Token 权限控制、Sentinel 流控
2. [AI 答题应用平台](https://www.codefather.cn/course/1790274408835506178)：React 跨端小程序开发，学习分库分表、分布式锁、SSE 实时推送、RxJava 响应式编程
3. [代码生成器共享平台](https://www.codefather.cn/course/1790980795074654209)：React + Spring Boot，学习命令行开发、模板引擎、Vert.x、设计模式、对象存储、性能优化
4. [SQL 数据生成平台（25年最新）](https://www.codefather.cn/course/1966416608870055938)：React + Spring Boot，学习 Schema 设计、Druid SQL 解析器、设计模式

更多项目：[鱼皮原创项目合集](https://www.codefather.cn/post/1797431216467001345)



### 学习资源

- ⭐ [鱼皮原创 React 项目教程](https://www.codefather.cn/post/1797431216467001345)：在实战中学习 React
- [TypeScript 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990753941477961730)：React 项目必学
- [Next.js 全栈开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754280717463553)：React SSR 框架



## 阶段 7：求职备战（面试前 1 个月突击）

### 学习目标

熟练掌握 React 常见面试题，准备好简历和项目经历，顺利通过面试。



### 学习建议

1）准备简历和项目：简历上一定要有 1-2 个完整的 React 项目（如果想增加前端竞争力，最好同时也要有 Vue 项目）。

写简历本身并不难，建议使用 [老鱼简历](https://www.laoyujianli.com/) 制作简历，不用浪费时间在调样式和模板上。

![老鱼简历网站简历模板大全](https://pic.yupi.icu/1/%E8%80%81%E9%B1%BC%E7%AE%80%E5%8E%86%E7%BD%91%E7%AB%99%E7%AE%80%E5%8E%86%E6%A8%A1%E6%9D%BF%E5%A4%A7%E5%85%A8.png)

关于如何写好简历，推荐学习鱼皮的 [保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)。

2）多刷面试题：React 的面试题主要包括基础概念、Hooks、虚拟 DOM、性能优化、状态管理等。建议使用 [面试鸭](https://www.mianshiya.com/) 刷题。

![面试鸭刷题神器前端八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8%E5%89%8D%E7%AB%AF%E5%85%AB%E8%82%A1%E6%96%87.png)

3）理解 React 的原理：面试时不仅要会用，还要理解原理。比如虚拟 DOM 的 Diff 算法、Hooks 的实现原理、Fiber 架构等。

4）对比 React 和 Vue：面试时可能会被问到 React 和 Vue 的区别，要能够说出各自的优势和适用场景。

更多求职干货：[编程导航求职干货分享](https://www.codefather.cn/learn?category=76&type=2)



### 经典面试题

**基础概念：**

1. React 有什么特点？
2. 什么是虚拟 DOM？有什么作用？
3. 什么是 JSX？
4. React 的单向数据流是什么？

**组件：**

1. 函数组件和类组件有什么区别？
2. Props 是什么？如何使用？
3. 受控组件和非受控组件有什么区别？

**Hooks：**

1. 什么是 Hooks？为什么要使用 Hooks？
2. useState 和 useEffect 如何使用？
3. useEffect 的依赖数组有什么作用？
4. useMemo 和 useCallback 有什么区别？
5. 如何自定义 Hooks？

**性能优化：**

1. React 如何进行性能优化？
2. 什么是 React.memo？
3. 虚拟 DOM 的 Diff 算法是怎样的？

**状态管理：**

1. Context API 如何使用？
2. Redux 的工作原理是什么？



### 面试题库

- ⭐ [React 基础面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900465338241026)
- [React 进阶面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900573026996225)
- [React Router 面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900635140444161)
- [React 状态管理面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900696662495234)
- [React 工具和库面试题 - 面试鸭](https://www.mianshiya.com/bank/1817900752874557442)



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
- ⭐ [React 官方文档](https://zh-hans.react.dev/)：最权威的学习资料
- [前端开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1789190394078011393)：完整的前端学习路线
- [JavaScript 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990753900055015426)：JavaScript 基础
- [TypeScript 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990753941477961730)：TypeScript 必学
- [Next.js 全栈开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754280717463553)：React SSR 框架
- [Vue.js 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754217060511746)：对比学习 Vue

### React 专题资源

- [React GitHub](https://github.com/facebook/react)：React 源码
- [awesome-react](https://github.com/enaqx/awesome-react)：React 资源大全
- [React Status](https://react.statuscode.com/)：React 周刊（英文）

### 技术博客

- [React 官方博客](https://react.dev/blog)：React 官方技术博客
- [Meta Engineering](https://engineering.fb.com/)：Meta 工程技术博客
- [腾讯 AlloyTeam](http://www.alloyteam.com/)：腾讯全端团队博客
- [淘宝前端团队 FED](https://fed.taobao.org/)：阿里淘宝前端团队



## 写在最后

React 是全球最流行的前端框架之一，掌握 React 能够让你有机会进入大厂工作。React 的学习曲线相对 Vue 更陡峭一些，但只要掌握了 JavaScript 基础和 Hooks，学习 React 并不难。

希望这份学习路线对大家有帮助，也感谢对鱼皮的支持，谢谢大家，加油！

![](https://pic.yupi.icu/1/%E9%B1%BC%E7%9A%AE%E6%84%9F%E8%B0%A2.png)



## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
