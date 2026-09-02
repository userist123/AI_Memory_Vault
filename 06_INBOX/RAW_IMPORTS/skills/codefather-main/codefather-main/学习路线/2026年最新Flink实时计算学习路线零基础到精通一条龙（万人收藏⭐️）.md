# 2026年最新Flink实时计算学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Flink 求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1837027634454786050)



## 开篇介绍

Apache Flink 是一个分布式流处理框架，专注于实时数据流处理。

可以说 Flink 是目前最流行的实时计算框架了，被阿里、腾讯、字节跳动等互联网巨头公司广泛使用。

**为什么要学 Flink？**

实时计算是大数据的重要方向，应用非常广泛。从实时推荐到实时风控、从实时监控到实时报表，各行各业都需要实时计算能力。而 Flink 又是实时计算的首选框架，因此掌握 Flink 是大数据工程师的重要技能。

会 Flink 的大数据攻城狮的薪资普遍较高，一线城市的 Flink 工程师平均薪资在 30-50K，资深 Flink 专家可以达到 60-100K+。

![](https://pic.yupi.icu/1/image-20251203120323592.png)

Flink 的核心特点是高吞吐量、低延迟、有状态计算。Flink 采用 **纯流式架构**，将批处理视为流处理的特例。Flink 的流处理是真正的流处理，而不是 Spark Streaming 的微批处理，因此延迟更低（毫秒级）。



### 学习前提

学习 Flink 建议先掌握：
1. Java/Scala/Python 编程：熟练使用 Java 或 Scala（Flink 主要语言）【必学】。关于 Java 的详细学习，可以查看 [Java 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1789190431398928386)
2. 大数据基础：理解分布式计算、流式处理等【建议】。关于大数据开发的详细学习，可以查看 [大数据开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755401435492353)
3. SQL 基础：熟练使用 SQL【建议】。关于 SQL 的详细学习，可以查看 [SQL 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755217309741058)

关于大数据开发的详细学习，可以查看 [大数据开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755401435492353)



### 学习路线图

![Flink 实时计算学习路线思维导图](https://pic.yupi.icu/roadmap/flink-realtime-computing-roadmap.png)



### 就业方向

学完 Flink 后，可以从事以下岗位：

1. 实时计算工程师：使用 Flink 开发实时计算应用
2. 大数据开发工程师：使用 Flink 处理流数据
3. 流式数据工程师：开发流式数据处理系统
4. Flink 架构师：设计 Flink 流式计算架构



## 整体学习建议

1）Flink 是大数据框架，要先理解大数据和流式计算的基本概念，再学习 Flink。

2）Flink 主要使用 Java 和 Scala 开发，Python（PyFlink）支持有限。建议使用 Java 学习 Flink。

3）Flink 的概念很多（如事件时间、水位线、窗口等），建议边学边写代码，通过实践理解概念。

![](https://pic.yupi.icu/1/image-20251203120530496.png)

4）Flink 的优势是低延迟，要学习如何设计和优化 Flink 作业，实现毫秒级延迟。

学习 Flink 时可以使用 [AI 资源导航](https://ai.codefather.cn/) 中的工具辅助理解概念、编写代码。

![AI资源大全网站](https://pic.yupi.icu/1/AI%E8%B5%84%E6%BA%90%E5%A4%A7%E5%85%A8%E7%BD%91%E7%AB%99.png)



## 阶段 1：Flink 基础（10-20 天，仅供参考）

### 学习目标

理解 Flink 的核心概念和架构。



### 知识点

**Flink 基础概念【必学】：**

- Flink 的特点和优势
- 流处理和批处理
- Flink 的架构（JobManager、TaskManager）
- Flink 和 Spark 的对比

**DataStream API【必学】：**

- Source、Transformation、Sink
- 基本转换操作（map、filter、flatMap）
- 聚合操作（keyBy、reduce、aggregate）

**时间和水位线【必学，核心重点】：**

- 事件时间（Event Time）
- 处理时间（Processing Time）
- 水位线（Watermark）



### 学习建议

1）Flink 的核心是 DataStream API，用于处理流数据。DataStream 是无界的数据流，可以进行各种转换操作。

![](https://pic.yupi.icu/1/levels_of_abstraction.svg)

2）时间和水位线是 Flink 的重要概念。事件时间是数据产生的时间，处理时间是数据被处理的时间。水位线用于处理乱序数据。

3）理解 Flink 的流式架构，Flink 是真正的流处理，而不是微批处理。



### 学习资源

- ⭐ [Apache Flink 官方文档](https://nightlies.apache.org/flink/flink-docs-release-2.1/zh/)：最权威的学习资料
- ⭐️ [尚硅谷大数据 Flink 教程](https://www.bilibili.com/video/BV1eg4y1V7AN)
- [什么是 Flink 实时计算](https://www.fanruan.com/finepedia/article/685bce8a0bd240a239ca72e3)：核心概念全解析
- [尚硅谷大数据 Flink 实时数仓项目5.0教程](https://www.bilibili.com/video/BV1Xb421E7jg)


## 阶段 2：Flink SQL（15-25 天，仅供参考）

Flink SQL 是 Flink 的重要发展方向，让开发者可以使用 SQL 开发流式应用，大大降低了实时计算的门槛。



### 学习目标

掌握 Flink SQL，能够使用 SQL 开发流式应用。



### 知识点

**Flink SQL 基础【必学】：**

- Table API
- Flink SQL 语法
- 流表转换
- 时间属性

**Flink SQL 高级特性【必学】：**

- 窗口（Tumbling、Sliding、Session）
- JOIN（Regular、Interval、Temporal）
- UDF

**Flink SQL 实战【必学】：**

- Connector（Kafka、MySQL、Hive）
- 实时数据分析
- 实时数仓



### 学习建议

1）Flink SQL 的窗口是核心概念，可以将无界流切分为有界窗口进行聚合计算。

2）Flink 2.1 引入了 ML_PREDICT 函数，可以在 Flink SQL 中调用机器学习模型，实现实时 AI 推理。



### 学习资源

- [Flink SQL 官方文档](https://nightlies.apache.org/flink/flink-docs-release-2.1/zh/docs/dev/table/sql/queries/overview/)：官方文档



## 阶段 3：状态和容错（10-20 天，仅供参考）

状态是 Flink 流处理的核心，可以存储中间计算结果。Flink 的状态管理非常强大，支持大规模状态存储。

Checkpoint 是 Flink 的容错机制，定期将状态保存到持久化存储。如果任务失败，可以从 Checkpoint 恢复。

![](https://pic.yupi.icu/1/from-aligned-to-unaligned-checkpoints-part-1-2.png)



### 学习目标

掌握 Flink 的状态管理和容错机制。



### 知识点

**状态管理【必学】：**

- Keyed State 和 Operator State
- State Backend（Memory、FS、RocksDB）
- 状态的TTL

**Checkpoint【必学】：**

- Checkpoint 的原理
- Savepoint
- 状态恢复



### 学习资源

- [使用 Apache Flink 进行实时流处理](https://developer.aliyun.com/article/1535880)：深入解析



## 阶段 4：项目实战（20-40 天，仅供参考）

### 学习目标

通过实际项目巩固所学知识，积累 Flink 项目经验。



### 学习建议

1）实时数据处理：使用 Flink 处理 Kafka 流数据，如实时日志分析、实时监控等。

2）实时数仓：使用 Flink 开发实时数据仓库，实现秒级数据更新。

3）实时推荐/风控：使用 Flink 开发实时推荐系统或风控系统。



### 项目推荐

- 实时日志分析
- 实时大屏监控
- 实时数据仓库
- 实时推荐系统

**优质开源项目：**

- ⭐ [Real-time Data Pipeline with Kafka, Flink](https://github.com/abeltavares/real-time-data-pipeline)：实时数据管道项目，整合 Kafka、Flink、Iceberg
- [Flink CDC](https://github.com/apache/flink-cdc)：5.9k+ stars，Flink 流式数据集成工具
- [Stream Processing with Flink](https://github.com/raycad/stream-processing)：Apache Flink 流处理指南和示例



### 学习资源

- [Flink 实时数仓实战](https://www.aliyun.com/sswb/950479.html)：阿里云教程
- [2025 流处理引擎对比](https://zhuanlan.zhihu.com/p/1902305468241670444)：Flink vs Kafka Streams vs Spark



## 阶段 5：求职备战（面试前 15 天突击）

### 学习目标

熟练掌握 Flink 常见面试题，准备好简历和项目经历，顺利通过面试。



### 学习建议

1）准备 Flink 项目：简历上一定要有 Flink 实时计算项目经历，如处理过的数据吞吐量、实现的延迟、优化过的性能等。面试时要能够清楚地介绍项目的业务场景、技术架构、实时性要求、遇到的挑战和解决方案。

2）准备简历：建议使用 [老鱼简历](https://www.laoyujianli.com/) 快速制作简历；关于如何写好简历，推荐学习鱼皮的 [保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)。

![老鱼简历网站简历模板大全](https://pic.yupi.icu/1/%E8%80%81%E9%B1%BC%E7%AE%80%E5%8E%86%E7%BD%91%E7%AB%99%E7%AE%80%E5%8E%86%E6%A8%A1%E6%9D%BF%E5%A4%A7%E5%85%A8.png)

3）多刷面试题：Flink 的面试题主要包括 Flink 架构、DataStream API、Flink SQL、状态管理、容错机制等，建议使用 [面试鸭](https://www.mianshiya.com/) 刷题，有很多企业高频大数据八股文面试题。

![面试鸭刷题神器热门八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8%E7%83%AD%E9%97%A8%E5%85%AB%E8%82%A1%E6%96%87.png)

4）面试时可能会被问到 Flink 和 Spark Streaming 的区别，要能够说出两者的优劣势：Flink 延迟更低（毫秒级），Spark 吞吐量更高。

更多求职干货：[编程导航求职干货分享](https://www.codefather.cn/learn?category=76&type=2)



### 经典面试题

**Flink 基础：**

1. Flink 是什么？有什么特点？
2. Flink 和 Spark Streaming 有什么区别？
3. Flink 的架构是怎样的？JobManager 和 TaskManager 的作用是什么？
4. Flink 的流处理和批处理有什么区别？

**时间和水位线：**

1. 事件时间和处理时间有什么区别？
2. 什么是水位线（Watermark）？有什么作用？
3. 如何处理乱序数据？
4. 如何处理延迟数据？

**窗口：**

1. Flink 的窗口有哪些类型？
2. Tumbling Window 和 Sliding Window 有什么区别？
3. Session Window 是什么？
4. 如何触发窗口计算？

**状态和容错：**

1. 什么是状态？Flink 的状态有哪些类型？
2. 什么是 Checkpoint？工作原理是什么？
3. Checkpoint 和 Savepoint 有什么区别？
4. State Backend 有哪些类型？各有什么特点？

**Flink SQL：**

1. Flink SQL 和 Spark SQL 有什么区别？
2. 如何在 Flink SQL 中定义时间属性？
3. Flink SQL 的 JOIN 有哪些类型？
4. 如何优化 Flink SQL 性能？



### 面试题库

- ⭐ [Flink 面试题 - 面试鸭](https://www.mianshiya.com/bank/1837027634454786050)
- [大数据面试题库大全 - 面试鸭](https://www.mianshiya.com/banks?category=bigdata)



### 求职资源

- ⭐ [鱼皮的保姆级求职指南](https://www.codefather.cn/course/job)：从简历到面试的完整指南
- ⭐ [鱼皮的保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)：如何写出高质量简历
- [编程导航的求职干货分享](https://www.codefather.cn/learn?category=76&type=2)：求职经验和技巧
- [老鱼简历](https://www.laoyujianli.com/)：写简历工具 + 简历模板大全
- [真实面经大全](https://www.codefather.cn/job/experience)：了解真实的面试流程
- [几百场真实面试视频](https://www.codefather.cn/mockInterview)：观看他人的面试过程
- [1 对 1 模拟面试](https://ai.mianshiya.com/)：AI 模拟面试练习
- [面试题讲解视频](https://space.bilibili.com/3546383483144655)：面试鸭官方题解



## 其他学习资源

### 知识总结

- ⭐ [编程导航](https://www.codefather.cn/)：学习路线、项目教程、面试题、编程资源一站式平台
- [大数据开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755401435492353)：完整的大数据路线

### Flink 资源

- [Apache Flink 官网](https://flink.apache.org/)：官方网站
- [Flink 中文文档](https://nightlies.apache.org/flink/flink-docs-release-2.1/zh/)：中文文档

### 技术博客

- [Apache Flink 官方博客](https://flink.apache.org/blog/)：Flink 官方技术博客
- [Alibaba Cloud Blog](https://www.alibabacloud.com/blog)：阿里云 Flink 实践
- [Uber Engineering Blog](https://www.uber.com/blog/engineering/)：Uber 实时计算
- [Netflix TechBlog](https://netflixtechblog.com/)：Netflix 流处理



---



OK 就写到这里，感谢大家的阅读，希望这份路线对大家有帮助，加油！

![](https://pic.yupi.icu/1/bf98472e6e0a48ae9861f3f6c41762d3.jpeg)




## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
