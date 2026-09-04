# 2026年最新Elasticsearch学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Elasticsearch求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1805423815382736897)



## 开篇介绍

**Elasticsearch 是什么？**

简单来说，它是一个基于 Lucene 的开源搜索引擎，但这样说可能太抽象了。想象一下，当你在电商网站搜索 “鱼皮” 时，能够瞬间从海量商品中找到最匹配的结果，或者当你在日志系统中查找某个错误信息时，能够从亿级日志中秒级定位问题，这背后的功臣就是 Elasticsearch（简称 ES）。

ES 诞生于 2010 年，经过十多年的发展，已经成为全文搜索和数据分析领域的事实标准。它不仅仅是一个搜索引擎，更是一个分布式的、实时的文档存储和分析引擎。ES 的核心优势在于：分布式架构带来的高可用和高性能、近实时搜索能力、强大的全文检索功能、以及丰富的聚合分析能力。

**为什么要学 Elasticsearch？**

在大数据时代，几乎每个互联网公司都需要强大的搜索和分析能力。ES 在企业中的应用场景非常广泛，比如电商平台的商品搜索、社交媒体的内容搜索、日志分析和运维监控、实时数据分析、推荐系统等等。掌握 ES，不仅能让你在求职时更有竞争力，还能帮助你解决实际工作中的搜索和分析难题。

**ES 能解决什么问题？**

核心是 2 个字 —— 搜索。传统的关系型数据库（如 MySQL）虽然也能实现搜索功能，但在海量数据的全文检索场景下性能很差。ES 通过倒排索引、分词技术、分布式架构，能够在亿级数据中实现毫秒级的搜索响应。此外，ES 还提供了强大的聚合分析功能，可以像 SQL 的 GROUP BY 一样对数据进行统计分析，这让它不仅是搜索引擎，更是数据分析利器。

比如鱼皮的 [编程导航网站](https://codefather.cn/)，就使用了 ES 做搜索，更灵活：

![](https://pic.yupi.icu/1/image-20251202111902736.png)

在 AI 时代，ES 的应用场景更加广泛。ES 8.0 开始原生支持向量搜索，可以用于 AI 应用的语义搜索、推荐系统、相似度计算等场景。学习 ES 之后，能帮助你更快开发出更准确的 RAG 知识库检索应用。



### 学习路线图

![Elasticsearch 学习路线思维导图](https://pic.yupi.icu/roadmap/elasticsearch-roadmap.png)



### 就业方向

ES 是后端开发、大数据开发、运维工程师等岗位的重要技能，掌握 ES 会增加你在下面这些岗位的竞争力：

1. Java 后端开发工程师：使用 ES 实现商品搜索、内容搜索等功能
2. 大数据开发工程师：使用 ES 进行日志分析、数据可视化。关于大数据开发的详细学习，可以查看 [大数据开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755401435492353)
3. 全栈开发工程师：使用 ES 构建全文搜索功能
4. 运维工程师：使用 ELK（Elasticsearch + Logstash + Kibana）进行日志收集和监控
5. 数据分析师：使用 ES 进行数据统计和分析



## 整体学习建议

1）ES 的学习曲线相对平缓，建议先快速上手（安装 ES、创建索引、插入数据、简单查询），体验 ES 的强大功能，然后再深入学习原理。

2）Kibana 是 ES 的可视化工具，提供了 Dev Tools 控制台，可以方便地执行 ES 命令。学习 ES 时强烈建议使用 Kibana，而不是直接用 HTTP 请求。

![](https://pic.yupi.icu/1/1725504293951-9e55779d-9187-4a18-bea5-f23f187a9197.png)

3）注意版本选择

ES 更新非常快，不同版本之间有一些差异。建议使用 7.x 或 8.x 版本，这两个版本是目前的主流版本。本路线以 7.x 版本为主，兼容 8.x。

4）学习 ES 时可以多用 AI 工具（如 ChatGPT、豆包）辅助理解概念、生成查询语句、调试问题。可以到鱼皮的 [AI 资源大全](https://ai.codefather.cn/) 中找到更多 AI 工具。

![AI资源大全网站](https://pic.yupi.icu/1/AI%E8%B5%84%E6%BA%90%E5%A4%A7%E5%85%A8%E7%BD%91%E7%AB%99.png)



## 阶段 1：Elasticsearch 基础入门（3-5 天）

### 学习目标

理解 ES 的基本概念和核心原理，能够安装和启动 ES。



### 知识点

**搜索引擎基础【必学】：**

- 全文搜索的基本原理
- 倒排索引（Inverted Index）的概念
- 分词（Tokenization）的作用
- 为什么 ES 比 MySQL 查询快

**ES 核心概念【必学】：**

- 索引（Index）：类似数据库的表
- 文档（Document）：类似数据库的行
- 字段（Field）：类似数据库的列
- 映射（Mapping）：类似数据库的 Schema
- 分片（Shard）：数据分片和副本
- 节点（Node）：ES 服务器实例
- 集群（Cluster）：多个节点组成的集群

**ES vs 关系型数据库【必学】：**

| ES | MySQL |
|---|---|
| Index | Database/Table |
| Document | Row |
| Field | Column |
| Mapping | Schema |
| Shard | Partition |

**ES 的应用场景【必学】：**

- 全文搜索：电商搜索、内容搜索
- 日志分析：ELK 日志分析系统
- 数据分析：实时数据统计
- 监控告警：系统监控和告警
- 推荐系统：基于相似度的推荐



### 学习建议

1）倒排索引是 ES 的核心，必须理解它的工作原理。简单来说，倒排索引是 "词 → 文档" 的映射，而正排索引是 "文档 → 词" 的映射。倒排索引让搜索变得非常快。

2）ES 的概念和关系型数据库类似但不完全相同，不要生搬硬套。ES 是文档型数据库，没有表之间的关联关系（Join），这是它和 MySQL 最大的区别。

和数据库类比：

| **Elasticsearch 概念** | **关系型数据库类比** |
| ---------------------- | -------------------- |
| Index                  | Table                |
| Document               | Row                  |
| Field                  | Column               |
| Mapping                | Schema               |
| Shard                  | Partition            |
| Replica                | Backup               |

3）这个阶段不需要深入学习集群和分片，先理解单机模式下 ES 的基本概念即可。



### 学习资源

- ⭐ [Elasticsearch 官方文档](https://www.elastic.co/guide/en/elasticsearch/reference/current/index.html)：最权威的学习资源
- [Elasticsearch 入门教程](https://www.bilibili.com/video/BV1KmMqzsEU3/)：快速入门，讲解清晰



## 阶段 2：安装和环境搭建（1 天）

### 学习目标

在本地或云上成功搭建 ES 环境，能够通过 Kibana 操作 ES。

具体来说有 3 个任务：

- 在本地搭建 ES + Kibana 环境
- 安装 IK 分词器并测试分词效果
- 使用 Kibana Dev Tools 执行简单的查询



### 知识点

**ES 安装【必学】：**

- Windows/Mac/Linux 系统下的安装
- Docker 方式安装（推荐）
- 云 ES 服务使用（推荐新手）

**Kibana 安装【必学】：**

- Kibana 的作用
- Kibana 安装和配置
- Kibana Dev Tools 的使用

**中文分词器【必学】：**

- IK 分词器的安装
- ik_smart vs ik_max_word
- 自定义词典（可选）

**版本选择【必学】：**

- ES 7.x vs 8.x
- 版本兼容性问题
- 如何选择合适的版本



### 学习建议

1）推荐使用 Docker 安装 ES 和 Kibana，这是最简单的方式。如果不熟悉 Docker，可以直接下载压缩包安装，或者使用云 ES 服务（阿里云、腾讯云都有）。

2）IK 中文分词器是必装的，ES 默认的分词器不支持中文。安装后要测试一下分词效果，理解 ik_smart 和 ik_max_word 的区别。

3）Kibana 的 Dev Tools 是学习 ES 的最佳工具，一定要安装。它提供了语法高亮、自动补全、历史记录等功能，比直接用 curl 命令方便太多了。

4）版本选择建议使用 7.17（7.x 的最后一个版本，最稳定）或者 8.x 的最新版本。不建议使用 6.x 及以下版本，太老了。



### 学习资源

- [IK 分词器安装](https://github.com/medcl/elasticsearch-analysis-ik)：IK 分词器官方文档



## 阶段 3：索引和文档操作（3-7 天）

### 学习目标

掌握 ES 的索引和文档操作，能够创建索引、插入和查询文档。



### 知识点

**索引管理【必学】：**

- 创建索引（Create Index）
- 删除索引（Delete Index）
- 查看索引（Get Index）
- 索引设置（Index Settings）

**Mapping 映射【必学】：**

- 什么是 Mapping
- 动态映射 vs 静态映射
- 常用的字段类型（text、keyword、long、date 等）
- text vs keyword 的区别
- 如何定义 Mapping

**文档操作【必学】：**

- 插入文档（Index Document）
- 更新文档（Update Document）
- 删除文档（Delete Document）
- 批量操作（Bulk API）

**常用字段类型【必学】：**

- text：全文检索字段
- keyword：精确匹配字段
- long/integer：数字类型
- date：日期类型
- boolean：布尔类型
- object/nested：对象类型【建议学】
- geo_point：地理位置【可不学】



### 学习重点

1）Mapping 是 ES 的核心概念，相当于数据库的 Schema。动态映射虽然方便，但在生产环境中建议使用静态映射，因为动态映射可能会导致字段类型不符合预期。

2）text 和 keyword 是最容易搞混的两个类型。text 用于全文检索，会被分词；keyword 用于精确匹配和聚合，不会被分词。一个字段可以同时是 text 和 keyword 类型（multi-field）。

3）批量操作（Bulk API）在导入大量数据时非常有用，要掌握其使用方法。Bulk API 比单条插入快很多。

4）Mapping 一旦创建就不能修改字段类型，只能新增字段或重建索引。这是 ES 的一个重要限制，创建 Mapping 时要慎重。



### 经典面试题

1. ES 的 Mapping 是什么？如何定义 Mapping？
2. text 和 keyword 有什么区别？什么时候用 text，什么时候用 keyword？
3. ES 如何实现倒排索引？
4. ES 的分词器有哪些？如何自定义分词器？



### 学习资源

- [ES Mapping 详解](https://www.elastic.co/guide/en/elasticsearch/reference/current/mapping.html)：官方 Mapping 文档



### 小试牛刀

- 创建一个商品索引，定义 Mapping（包含商品名称、价格、分类、描述等字段）
- 批量导入商品数据
- 查询和更新商品信息



## 阶段 4：查询 DSL（3-10 天）


查询 DSL（Domain Specific Language）是 Elasticsearch 的查询语言，支持全文检索、精确匹配、范围查询等多种查询方式。



### 学习目标

掌握 ES 的查询 DSL，能够编写复杂的查询语句。



### 知识点

**基本查询【必学】：**

- match：全文检索查询
- term：精确匹配查询
- terms：多值精确匹配
- range：范围查询
- exists：字段存在查询
- prefix：前缀查询
- wildcard：通配符查询【建议学】
- fuzzy：模糊查询【建议学】

**复合查询【必学】：**

- bool 查询：must、should、must_not、filter
- bool 查询的评分规则
- constant_score：固定分数查询【建议学】

**全文检索【必学】：**

- match_all：匹配所有文档
- match：单字段全文检索
- multi_match：多字段全文检索
- match_phrase：短语匹配
- match_phrase_prefix：短语前缀匹配【建议学】

**过滤查询【必学】：**

- filter context vs query context
- 过滤查询的性能优化
- 缓存机制

**排序和分页【必学】：**

- sort：排序
- from/size：分页
- scroll：深度分页【建议学】
- search_after：深度分页的更好方案【建议学】

**高亮显示【建议学】：**

- highlight：高亮显示搜索关键词
- 自定义高亮标签



### 学习建议

1）match 和 term 是最基本也是最常用的查询，一定要理解它们的区别。match 会对查询词分词，用于全文检索；term 不会分词，用于精确匹配。

2）bool 查询是最强大的查询方式，可以组合多个查询条件。must、should、must_not 的区别要搞清楚：must 必须匹配且参加评分，should 可以匹配且参加评分，must_not 必须不匹配且不参加评分，filter 必须匹配但不参加评分。

3）filter context 和 query context 的区别很重要，关系到查询性能。filter context 的查询结果会被缓存，性能更好，适合精确匹配；query context 的查询结果会参加评分，适合全文检索。

4）分页查询有坑：from/size 方式在深度分页时性能很差（from 很大时），建议使用 scroll 或 search_after。但实际工作中，大部分场景不需要深度分页。

5）ES 的查询 DSL 比较复杂，建议边学边练。可以用 Kibana Dev Tools 实时测试查询效果，观察返回结果。

![](https://pic.yupi.icu/1/1725519517032-9392b13d-f1d2-4792-a89d-626cda36230b.png)



### 经典面试题

1. match 和 term 有什么区别？
2. bool 查询的 must、should、must_not、filter 有什么区别？
3. filter context 和 query context 有什么区别？
4. ES 如何实现分页？深度分页有什么问题？



### 学习资源


- [ES 官方查询文档](https://www.elastic.co/guide/en/elasticsearch/reference/current/query-dsl.html)：官方查询 DSL 文档



### 牛刀小试

- 在商品索引中实现关键词搜索（支持分词）
- 实现价格范围过滤
- 实现多条件组合查询（如搜索 "花生" + 价格在 100-500 之间 + 品牌是 "鱼皮"）
- 实现搜索结果高亮显示



## 阶段 5：聚合分析（3-7 天）


聚合分析是 Elasticsearch 的强大功能，可以对数据进行统计、分组、计算等操作，类似于 SQL 的 GROUP BY 和聚合函数。



### 学习目标

掌握 ES 的聚合功能，能够进行数据统计和分析。



### 知识点

**聚合类型【必学】：**

- 指标聚合（Metrics Aggregation）：求和、平均值、最大值、最小值等
- 桶聚合（Bucket Aggregation）：分组统计
- 管道聚合（Pipeline Aggregation）：对聚合结果再聚合【建议学】

**常用指标聚合【必学】：**

- avg：平均值
- sum：求和
- min/max：最小值/最大值
- stats：统计信息（包含 count、sum、avg、min、max）
- cardinality：基数统计（类似 SQL 的 COUNT DISTINCT）

**常用桶聚合【必学】：**

- terms：分组聚合（类似 SQL 的 GROUP BY）
- range：范围聚合
- date_histogram：日期直方图聚合
- histogram：数值直方图聚合【建议学】

**嵌套聚合【必学】：**

- 桶聚合 + 指标聚合
- 多层嵌套聚合



### 学习建议

1）聚合是 ES 的强大功能，可以实现类似 SQL 的 GROUP BY、COUNT、AVG 等统计功能。要理解指标聚合和桶聚合的区别：指标聚合是对数值进行计算，桶聚合是对数据进行分组。

2）terms 聚合是最常用的聚合方式，相当于 SQL 的 GROUP BY。要注意 terms 聚合默认只返回 top 10 的桶，如果需要更多结果需要设置 size 参数。

3）date_histogram 聚合在时间序列数据分析中非常有用，可以按天、周、月等时间间隔进行聚合。

4）聚合可以嵌套使用，实现复杂的统计分析。比如先按分类聚合，然后对每个分类计算平均价格，这就是桶聚合 + 指标聚合的嵌套。



### 经典面试题

1. ES 的聚合有哪几种类型？各有什么用途？
2. terms 聚合和 cardinality 聚合有什么区别？
3. 如何实现类似 SQL 的 GROUP BY 功能？
4. ES 聚合的性能如何？有什么优化方法？



### 学习资源

- [ES 官方聚合文档](https://www.elastic.co/guide/en/elasticsearch/reference/current/search-aggregations.html)：官方聚合文档



### 牛刀小试

- 统计商品总数、平均价格、最高价格、最低价格
- 按分类聚合，统计每个分类的商品数量和平均价格
- 按价格区间聚合，统计不同价格区间的商品数量
- 按日期聚合，统计每天新增的商品数量



## 阶段 6：集群架构和优化（4-10 天）

### 学习目标

理解 ES 的集群架构，掌握 ES 的性能优化方法。



### 知识点

**集群架构【必学】：**

- 节点角色：Master、Data、Coordinating、Ingest
- 分片和副本的概念
- 主分片和副本分片
- 分片分配策略
- 集群健康状态（Green、Yellow、Red）

**数据写入流程【必学】：**

- 文档写入的过程
- translog 的作用
- refresh 和 flush 的区别
- segment 的概念

**数据查询流程【必学】：**

- 查询阶段（Query Phase）
- 获取阶段（Fetch Phase）
- 协调节点的作用

**性能优化【建议学】：**

- 索引优化：合理设置分片数、使用批量导入、调整 refresh_interval
- 查询优化：使用 filter context、合理使用分词器、避免深度分页
- 硬件优化：SSD、内存、CPU 的选择
- JVM 调优：堆内存设置

**监控和运维【建议学】：**

- 集群监控指标
- 慢查询日志
- 索引生命周期管理（ILM）【可不学】



### 学习建议

1）集群架构是 ES 的核心，但在学习阶段不需要搭建真正的集群，理解概念即可。如果有条件，可以在本地用 Docker 启动 3 个 ES 节点组成集群。

2）分片数的设置很重要，过多或过少都会影响性能。一般建议：单分片大小控制在 20-50GB，分片数 = 数据量 / 单分片大小。但实际工作中，分片数还要考虑查询 QPS、节点数等因素。

![](https://pic.yupi.icu/1/image-20251202113023113.png)

3）refresh_interval 的设置会影响数据的实时性和写入性能。默认是 1 秒，意味着写入的数据最多 1 秒后才能被搜索到。如果对实时性要求不高，可以调大这个值来提升写入性能。

4）性能优化是一个持续的过程，要根据实际的性能瓶颈进行优化。常见的性能问题包括：查询慢、写入慢、集群不稳定等。



### 经典面试题

1. ES 的集群架构是怎样的？节点有哪些角色？
2. 什么是分片？什么是副本？它们有什么作用？
3. ES 的数据写入流程是怎样的？
4. ES 的数据查询流程是怎样的？
5. 如何优化 ES 的查询性能？



### 学习资源

- [ES 性能优化实践](https://www.elastic.co/guide/en/elasticsearch/reference/current/tune-for-indexing-speed.html)：官方性能优化文档



## 阶段 7：客户端编程（3-7 天）


客户端编程是通过编程语言（比如 Java、Python）操作 Elasticsearch 的方式，实现数据的增删改查和复杂业务逻辑。



### 学习目标

掌握在 Java、Python 等语言中使用 ES 的方法，能够将 ES 集成到实际项目中。

💡 根据实际需要，这个阶段也可以提前学习。



### 知识点

**Java 客户端【必学】：**

- REST High Level Client（7.x 推荐）
- Java API Client（8.x 推荐）
- Spring Data Elasticsearch
- 客户端的连接和配置
- 索引、文档、查询、聚合操作的 API

**Python 客户端【建议学】：**

- elasticsearch-py
- 基本的 CRUD 操作
- 查询和聚合操作

**数据同步方案【必学】：**

- 定时任务同步
- 双写方案
- Logstash 数据同步管道【建议学】
- 监听 MySQL Binlog（Canal）【建议学】



### 学习建议

1）Java 客户端的选择要根据 ES 版本。ES 7.x 推荐使用 REST High Level Client，ES 8.x 推荐使用 Java API Client。Spring Data Elasticsearch 的集成和使用更简单，但灵活性较差。

2）数据同步是 ES 实际应用中的关键问题。一般情况下，数据存储在 MySQL 中，ES 只是搜索引擎。如何保证 MySQL 和 ES 的数据一致性，有多种方案，要根据实际需求选择。

3）编程导航的聚合搜索项目和面试刷题项目都有 ES 实战，建议跟着项目动手实践，这是最好的学习方式。



### 练手项目

- ⭐ [编程导航 - 聚合搜索项目](https://www.codefather.cn/course/1790979621621641217)：使用 ES 实现聚合搜索功能，包含完整代码
- ⭐ [编程导航 - 面试刷题项目](https://www.codefather.cn/course/1789962114394353666)：使用 ES 实现题目搜索功能，包含完整代码



## 阶段 8：求职备战

### 学习目标

准备 ES 相关的面试题，整理项目经验，为求职做准备。



### 经典面试题

**基础概念：**

1. Elasticsearch 是什么？有什么特点？
2. Elasticsearch 和 MySQL 有什么区别？什么时候用 ES，什么时候用 MySQL？
3. 什么是倒排索引？ES 如何实现倒排索引？
4. ES 的分词器有哪些？如何自定义分词器？

**索引和查询：**

1. Mapping 是什么？如何定义 Mapping？
2. text 和 keyword 有什么区别？
3. match 和 term 有什么区别？
4. bool 查询的 must、should、must_not、filter 有什么区别？
5. filter context 和 query context 有什么区别？

**集群和性能：**

1. ES 的集群架构是怎样的？
2. 什么是分片？什么是副本？
3. ES 的数据写入流程是怎样的？
4. ES 的数据查询流程是怎样的？
5. 如何优化 ES 的查询性能？

**数据同步：**

1. MySQL 和 ES 如何进行数据同步？
2. 如何保证 MySQL 和 ES 的数据一致性？



### 面试题库

- ⭐ [Elasticsearch 面试题 - 面试鸭](https://www.mianshiya.com/bank/1805423815382736897)：全面的 ES 面试题
- [Elasticsearch 面试题 - 面试鸭](https://www.mianshiya.com/bank/1805423815382736897)：Elasticsearch 相关面试题



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

### 官方文档

- ⭐ [Elasticsearch 官方文档](https://www.elastic.co/guide/en/elasticsearch/reference/current/index.html)：最权威的学习资源
- [Kibana 官方文档](https://www.elastic.co/guide/en/kibana/current/index.html)：Kibana 官方文档
- [Logstash 官方文档](https://www.elastic.co/guide/en/logstash/current/index.html)：Logstash 官方文档

### 知识总结

- ⭐ [编程导航](https://www.codefather.cn/)：学习路线、项目教程、面试题、编程资源一站式平台
- [大数据学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755401435492353)：了解 ES 在大数据中的应用

### 技术博客

- [Elastic 官方博客](https://www.elastic.co/cn/blog)：Elasticsearch 官方博客
- [Elastic 中文社区](https://elasticsearch.cn/)：ES 中文社区
- [Uber Engineering Blog](https://www.uber.com/blog/engineering/)：Uber 搜索架构
- [LinkedIn Engineering](https://www.linkedin.com/blog/engineering)：LinkedIn 搜索实践

### 书籍推荐

- 《Elasticsearch 权威指南》：ES 官方出品，适合深入学习
- 《Elasticsearch 实战》：实战案例丰富



## 最后

Elasticsearch 是现代搜索和数据分析的核心技术，掌握 ES 不仅能让你在求职时更有竞争力，还能帮助你解决实际工作中的搜索和分析难题。

学习 ES 建议先理解基本概念和原理，再动手实践。强烈推荐通过编程导航的聚合搜索项目和面试刷题项目进行实战练习，鱼皮从 0 到 1 给大家讲解了概念，并立刻应用到项目中实战，纵享丝滑~

希望这份学习路线能够帮助大家系统地掌握 Elasticsearch 技术，为后续开发搜索和分析系统打下坚实的基础。

加油小伙伴们 💪🏻!

![](https://pic.yupi.icu/1/%E9%B1%BC%E7%9A%AE%E6%84%9F%E8%B0%A2.png)



## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
