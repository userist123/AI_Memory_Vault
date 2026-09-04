# 2026年最新PostgreSQL数据库学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



PostgreSQL 数据库求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1812070206546247681)



## 开篇介绍

**PostgreSQL 是什么？**

PostgreSQL（简称 PG）是世界上最先进的开源关系型数据库之一，它以其强大的功能、高度的可扩展性、严格的数据完整性和优秀的性能而闻名，被称为 “数据库界的瑞士军刀”。

![](https://pic.yupi.icu/1/1752389344902-504b6e0f-03a4-4cc1-957d-af3b9be8743a.png)

PostgreSQL 支持完整的 SQL 标准，提供了事务、外键、视图、触发器、存储过程等高级特性。它不仅是一个关系型数据库，还支持 JSON、XML、数组等非关系型数据类型，甚至可以作为文档数据库使用。PostgreSQL 的扩展性非常强，你可以自定义数据类型、函数、操作符，甚至可以用多种编程语言（Python、JavaScript、C 等）编写存储过程。

**为什么要学 PostgreSQL？**

虽然大家一直以为 MySQL 是最流行的开源数据库，但 PostgreSQL 在很多方面都超越了 MySQL，特别是在数据完整性、复杂查询、并发控制等方面。而且早在 Stack Overflow  2024 年开发者调查报告中，PostgreSQL 已经取代了 MySQL 成为目前最流行的数据库，比例甚至高出 MySQL 8%，差距不小。

![](https://pic.yupi.icu/1/1752388521933-8bdbcfac-8a95-4987-ba08-f73b533b6942.png)

PostgreSQL 是很多大型企业和互联网公司的首选数据库，比如 Instagram、Reddit、Uber、Apple 等都在使用 PostgreSQL。掌握 PostgreSQL，不仅能让你在求职时更有竞争力，还能帮助你处理更复杂的业务场景。

在 AI 时代，PostgreSQL 的向量扩展（pgvector）让其成为 AI 应用的理想数据库，可以存储和检索嵌入向量（embeddings），支持语义搜索、推荐系统等 AI 应用。学习 PostgreSQL，将为你的 AI 应用开发提供很大的方便。



### 学习路线图

![PostgreSQL 数据库学习路线思维导图](https://pic.yupi.icu/roadmap/postgresql-database-roadmap.png)



### 就业方向

PostgreSQL 相关的岗位和 MySQL 类似，主要包括：

1. 数据库管理员（DBA）：负责 PostgreSQL 数据库的管理、维护、备份恢复、性能优化
2. 后端开发工程师：使用 PostgreSQL 作为数据存储，开发业务系统
3. 数据开发工程师：编写复杂的 SQL 查询，进行数据处理和分析
4. 数据分析师：使用 PostgreSQL 进行数据分析和报表开发
5. 大数据工程师：使用 PostgreSQL 进行数据仓库建设
6. AI 工程师：使用 PostgreSQL + pgvector 开发 AI 应用



## 整体学习建议

1）边学边练是王道：数据库学习最忌讳只看不练。一定要跟着教程把每个 SQL 语句都敲一遍，建议使用鱼皮的 [SQL 自学网](https://sqlmother.yupi.icu/) 进行在线实战练习。

![鱼皮的SQL学习网](https://pic.yupi.icu/1/%E9%B1%BC%E7%9A%AE%E7%9A%84SQL%E5%AD%A6%E4%B9%A0%E7%BD%91.png)

2）先会用再深入：对于绝大多数同学来说，先把前 3 个阶段（PostgreSQL 基础、SQL 实战、高级特性）学完就足够了，能满足日常开发需求。性能优化和运维部分可以在实际工作中遇到问题时再深入学习。

3）PostgreSQL 和 MySQL 的区别：如果你已经学过 MySQL，那么 PostgreSQL 的基础部分可以快速过一遍，重点学习 PostgreSQL 特有的高级特性（如窗口函数、CTE、JSON 操作、全文搜索等）。关于 MySQL 数据库的详细学习，可以查看 [MySQL 数据库学习路线](https://www.codefather.cn/course/1789189862986850306/section/1789190581420793858)。

4）善用 AI 工具辅助学习：学习 PostgreSQL 时可以用 AI 工具（如 ChatGPT、Claude、Kimi 等）来辅助理解概念、生成 SQL 语句、优化 SQL 性能。更多 AI 工具推荐可以看 [AI 导航](https://ai.codefather.cn/)。

5）多看官方文档：PostgreSQL 的官方文档非常详细，而且中文翻译质量很高。遇到问题时，优先查阅官方文档。



## 阶段 1：PostgreSQL 基础（12-20 天，仅供参考）

这个阶段要掌握 PostgreSQL 的基本概念和基础操作。

### 学习目标

理解关系型数据库的基本概念，掌握 PostgreSQL 的安装和基本操作，能够通过命令行或可视化工具来创建数据库、设计表结构、进行简单的增删改查操作。

### 知识点

**数据库基本概念【必学】：**

- 关系型数据库的基本概念
- 数据库、表、字段、记录的概念
- 主键、外键、索引
- SQL 语言分类（DDL、DML、DQL、DCL）。关于 SQL 的详细学习，可以查看 [SQL 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755217309741058)

**PostgreSQL 安装和配置【必学】：**

- PostgreSQL 的安装（Windows、macOS、Linux）
- 初始化数据库集群
- 配置文件（postgresql.conf、pg_hba.conf）
- PostgreSQL 服务的启动和停止

**数据库操作【必学】：**

- 创建数据库（CREATE DATABASE）
- 删除数据库（DROP DATABASE）
- 查看数据库列表（\l）
- 切换数据库（\c）

**数据表操作【必学】：**

- 创建表（CREATE TABLE）
- 修改表结构（ALTER TABLE）
- 删除表（DROP TABLE）
- 查看表结构（\d）

**数据类型【必学】：**

- 数字类型（INTEGER、BIGINT、NUMERIC、REAL、DOUBLE PRECISION）
- 字符类型（CHAR、VARCHAR、TEXT）
- 日期时间类型（DATE、TIME、TIMESTAMP、INTERVAL）
- 布尔类型（BOOLEAN）
- JSON 类型（JSON、JSONB）【建议学】
- 数组类型（ARRAY）【建议学】

**数据操作【必学】：**

- 插入数据（INSERT）
- 删除数据（DELETE）
- 修改数据（UPDATE）
- 查询数据（SELECT）

**约束【必学】：**

- 主键约束（PRIMARY KEY）
- 外键约束（FOREIGN KEY）
- 唯一约束（UNIQUE）
- 非空约束（NOT NULL）
- 检查约束（CHECK）
- 默认值（DEFAULT）

**客户端工具【建议学】：**

- psql：PostgreSQL 命令行工具
- pgAdmin：PostgreSQL 官方图形化工具
- DBeaver：通用数据库管理工具
- DataGrip：JetBrains 出品的数据库工具



### 学习建议

1）PostgreSQL 的安装相对简单，建议在本地安装一个 PostgreSQL 数据库进行练习。如果不想安装，也可以使用 Docker 快速启动一个 PostgreSQL 容器。

2）psql 命令行工具是 PostgreSQL 的官方客户端，功能强大但需要记忆命令。建议先熟悉 psql 的基本命令，然后再使用图形化工具（如 pgAdmin、DBeaver）。

3）PostgreSQL 的数据类型比 MySQL 丰富很多，特别是 JSON、JSONB、数组等类型，非常实用。建议重点学习这些高级数据类型。

4）这个阶段的学习以实践为主，要多敲代码、多练习。可以参考官方文档中的教程示例，自己动手创建数据库、创建表、插入数据。

![](https://pic.yupi.icu/1/image-20251129133400696.png)



### 学习资源

- ⭐ [PostgreSQL 官方中文文档](http://www.postgres.cn/docs/current/index.html)：最权威的学习资料
- ⭐ [零基础入门 PostgreSQL 教程 2025 最新版](https://www.bilibili.com/video/BV1Wp4y1K7QN/)：系统教程
- [PostgreSQL 教程 - Redrock Postgres](https://www.rockdata.net/zh-cn/tutorial/)：详细的教程



### 练习平台

- ⭐ [SQL 自学网 - 鱼皮原创](https://sqlmother.yupi.icu/)：在线刷 SQL 题（支持标准 SQL 的语法）



## 阶段 2：SQL 实战和进阶（10-15 天，仅供参考）

这个阶段要熟练掌握 SQL 语句的编写，能够应对各种复杂的查询需求。



### 学习目标

熟练掌握 SQL 语句的各种语法，能够独立编写复杂的多表查询、子查询、聚合查询等，能够根据业务需求快速写出高效的 SQL 语句。

![](https://pic.yupi.icu/1/1764309925307-eddf9ad2-582c-4b42-8806-de0ce5c1771c-20251129133429369.png)



### 知识点

**基础查询【必学】：**

- SELECT 语句
- WHERE 条件过滤
- ORDER BY 排序
- LIMIT 和 OFFSET 分页
- DISTINCT 去重

**聚合函数【必学】：**

- COUNT、SUM、AVG、MAX、MIN
- GROUP BY 分组
- HAVING 分组过滤

**多表查询【必学，面试重点】：**

- 内连接（INNER JOIN）
- 左连接（LEFT JOIN）
- 右连接（RIGHT JOIN）
- 全外连接（FULL OUTER JOIN）
- 交叉连接（CROSS JOIN）
- 自连接（Self JOIN）

**子查询【必学】：**

- WHERE 子查询
- FROM 子查询
- SELECT 子查询
- EXISTS 子查询

**集合操作【建议学】：**

- UNION（去重合并）
- UNION ALL（不去重合并）
- INTERSECT（交集）
- EXCEPT（差集）

**窗口函数【必学，PostgreSQL 特色】：**

- ROW_NUMBER、RANK、DENSE_RANK
- SUM OVER、AVG OVER
- LAG、LEAD
- FIRST_VALUE、LAST_VALUE

**通用表表达式（CTE）【建议学，PostgreSQL 特色】：**

- WITH 语句
- 递归 CTE

**SQL 函数【建议学】：**

- 字符串函数（CONCAT、SUBSTRING、LENGTH、UPPER、LOWER）
- 日期时间函数（NOW、CURRENT_DATE、EXTRACT、DATE_TRUNC）
- 数学函数（ROUND、CEIL、FLOOR、ABS）
- 条件函数（CASE WHEN、COALESCE、NULLIF）



### 学习重点

1）SQL 是数据库学习的核心，一定要多练习。建议使用 [SQL 自学网](https://sqlmother.yupi.icu/) 刷题，从简单到复杂逐步提升。

2）窗口函数和 CTE 是 PostgreSQL 的强大特性，相比 MySQL 更加灵活和强大。建议重点学习这两个特性，它们在数据分析和报表开发中非常常用。

3）多表查询（JOIN）是 SQL 的难点，也是面试的重点。建议多做练习，理解各种 JOIN 的区别和使用场景。

4）子查询虽然强大，但性能一般不如 JOIN。在实际开发中，能用 JOIN 就不要用子查询。

5）善用 EXPLAIN 分析 SQL 执行计划，理解 SQL 的执行过程，为后续的性能优化打基础。



### 学习资源

- ⭐ [SQL 自学网 - 鱼皮原创](https://sqlmother.yupi.icu/)：在线刷 SQL 题
- [PostgreSQL 窗口函数教程](https://www.postgresql.org/docs/current/tutorial-window.html)：官方教程
- [LeetCode 数据库题目](https://leetcode.cn/problemset/database/)：SQL 刷题



### 面试题库

- [SQL 基础查询面试题 - 面试鸭](https://www.mianshiya.com/bank/1813047253254930434)
- [SQL 进阶查询面试题 - 面试鸭](https://www.mianshiya.com/bank/1813047306893705217)
- [PostgreSQL 数据库面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070255982329858)



## 阶段 3：PostgreSQL 高级特性（12-20 天，仅供参考）

这个阶段要学习 PostgreSQL 特有的高级特性，发挥 PostgreSQL 的强大功能。

### 学习目标

掌握 PostgreSQL 的高级特性，能够使用视图、存储过程、触发器、JSON 操作、全文搜索等功能，能够根据业务需求选择合适的技术方案。



### 知识点

**视图（View）【必学】：**

- 创建视图（CREATE VIEW）
- 物化视图（MATERIALIZED VIEW）
- 视图的更新和删除

**索引【必学，面试重点】：**

- B-Tree 索引（默认）
- Hash 索引
- GiST 索引（地理空间数据）
- GIN 索引（全文搜索、JSON、数组）
- 部分索引（Partial Index）
- 表达式索引（Expression Index）
- 多列索引（Multi-column Index）

**事务【必学，面试重点】：**

- 事务的 ACID 特性
- BEGIN、COMMIT、ROLLBACK
- 事务隔离级别（Read Uncommitted、Read Committed、Repeatable Read、Serializable）
- MVCC（多版本并发控制）

**存储过程和函数【建议学】：**

- 创建函数（CREATE FUNCTION）
- PL/pgSQL 语言
- 存储过程（CREATE PROCEDURE）

**触发器【建议学】：**

- 创建触发器（CREATE TRIGGER）
- BEFORE 和 AFTER 触发器
- FOR EACH ROW 和 FOR EACH STATEMENT

**JSON 操作【必学，PostgreSQL 特色】：**

- JSON 和 JSONB 的区别
- JSON 数据的插入和查询
- JSON 操作符（->、->>、#>、#>>）
- JSON 函数（jsonb_set、jsonb_insert、jsonb_array_elements）
- JSON 索引（GIN 索引）

**数组操作【建议学】：**

- 数组的创建和查询
- 数组操作符（&&、@>、<@）
- 数组函数（array_append、array_prepend、unnest）

**全文搜索【建议学，PostgreSQL 特色】：**

- tsvector 和 tsquery 类型
- to_tsvector 和 to_tsquery 函数
- 全文搜索索引（GIN 索引）
- 中文分词（使用 zhparser 扩展）

**范围类型【可不学】：**

- 整数范围（INT4RANGE、INT8RANGE）
- 时间范围（TSRANGE、TSTZRANGE）
- 范围操作符（&&、@>、<@）

**枚举类型【可不学】：**

- 创建枚举类型（CREATE TYPE ... AS ENUM）
- 枚举类型的使用



### 学习建议

1）PostgreSQL 的高级特性非常丰富，**不需要全部学完**，可以根据实际需要选择性学习。视图、索引、事务、JSON 操作是必学的，其他的可以先了解概念，用到时再深入学习。

2）JSON 操作是 PostgreSQL 的一大特色，相比 MySQL 的 JSON 支持更加完善。JSONB 类型的查询性能非常好，在很多场景下可以替代 MongoDB 等文档数据库。

3）全文搜索是 PostgreSQL 的强大特性，可以实现类似 Elasticsearch 的搜索功能。如果你的应用需要搜索功能，可以优先考虑使用 PostgreSQL 的全文搜索，而不是引入 Elasticsearch。

4）存储过程和触发器虽然功能强大，但会增加系统的复杂度，建议谨慎使用。**在现代应用开发中，更推荐将业务逻辑放在应用层而不是数据库层。**

5）PostgreSQL 支持多种索引类型，要根据实际查询需求选择合适的索引类型。B-Tree 索引适合范围查询，GIN 索引适合全文搜索和 JSON 查询。



### 学习资源

- ⭐ [PostgreSQL 官方文档 - 高级特性](http://www.postgres.cn/docs/current/tutorial-advanced.html)：官方文档
- [PostgreSQL JSON 操作教程](https://www.postgresql.org/docs/current/datatype-json.html)：JSON 操作
- [PostgreSQL 全文搜索教程](https://www.postgresql.org/docs/current/textsearch.html)：全文搜索



### 面试题库

- [PostgreSQL 数据库面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070255982329858)
- [数据库性能优化面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070982775521282)



## 阶段 4：性能优化和运维（10-15 天，仅供参考）

这个阶段主要学习 PostgreSQL 的性能优化和运维相关的知识。

### 学习目标

理解 PostgreSQL 的执行原理和性能优化方法，能够分析和解决 PostgreSQL 性能问题，掌握 PostgreSQL 的备份恢复、主从复制等运维技能。



### 知识点

**性能优化【建议学，面试重点】：**

- EXPLAIN 执行计划分析
- 慢查询日志分析
- SQL 优化（避免全表扫描、优化 JOIN、减少子查询）
- 索引优化（合理创建索引、避免索引失效）
- 查询缓存和结果集缓存

**PostgreSQL 架构【建议学】：**

- PostgreSQL 进程模型
- 共享内存和缓冲区
- WAL（Write-Ahead Logging）
- VACUUM 和 AUTOVACUUM

**配置优化【建议学】：**

- shared_buffers：共享缓冲区大小
- work_mem：工作内存大小
- maintenance_work_mem：维护工作内存大小
- effective_cache_size：有效缓存大小
- max_connections：最大连接数

**备份和恢复【建议学】：**

- pg_dump：逻辑备份
- pg_basebackup：物理备份
- PITR（Point-In-Time Recovery）

**主从复制和高可用【建议学】：**

- 流复制（Streaming Replication）
- 逻辑复制（Logical Replication）
- 主从切换
- 读写分离

**分区表【可不学】：**

- 范围分区（Range Partitioning）
- 列表分区（List Partitioning）
- 哈希分区（Hash Partitioning）

**监控和诊断【可不学】：**

- pg_stat_activity：查看活动连接
- pg_stat_statements：查看 SQL 统计
- pg_locks：查看锁信息
- 监控工具（pgAdmin、Prometheus + Grafana）



### 学习建议

1）性能优化是数据库学习的重要内容，也是面试的重点。建议重点学习 EXPLAIN 执行计划分析和 SQL 优化，这是最实用的技能。

2）PostgreSQL 的 VACUUM 机制很重要，它负责回收被删除或更新的行所占用的空间。要理解 VACUUM 和 AUTOVACUUM 的工作原理，避免表膨胀问题。

3）主从复制和高可用是生产环境的必备技能。如果你想往 DBA 方向发展，建议深入学习主从复制的配置和管理。

4）配置优化需要根据实际的硬件资源和业务场景来调整，不要盲目套用别人的配置。建议先使用默认配置，遇到性能瓶颈时再优化。

5）这部分内容相对高级，如果你是后端开发工程师，可以先了解基本概念，工作中遇到问题再深入学习。如果你想往 DBA 方向发展，建议系统学习。



### 学习资源

- ⭐ [PostgreSQL 性能优化教程](https://www.postgresql.org/docs/current/performance-tips.html)：官方文档
- [PostgreSQL 主从复制教程](https://www.postgresql.org/docs/current/warm-standby.html)：官方文档



### 面试题库

- [PostgreSQL 数据库面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070255982329858)
- [数据库性能优化面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070982775521282)
- [数据库高可用面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070552811208705)



## 阶段 5：求职备战（面试前 1 个月突击）

这个阶段主要是为求职做准备，包括刷面试题、准备简历项目、模拟面试等。



### 学习目标

熟练掌握 PostgreSQL 常见面试题，能够流畅回答面试官的提问，准备好简历上的项目经历，顺利通过 PostgreSQL 相关的面试环节。



### 学习建议

1）提前做规划：推荐阅读鱼皮的 [保姆级求职指南](https://www.codefather.cn/course/job)，尽早做规划，建议至少提前 3 个月开始准备。可以先看看目标公司的招聘要求，了解对 PostgreSQL 的要求。

2）打磨简历：简历上一定要有能体现你 PostgreSQL 能力的项目经历，比如你做过什么系统、设计了哪些表、写过哪些复杂的 SQL、遇到过什么性能问题、是如何优化的等。

建议使用 [老鱼简历](https://www.laoyujianli.com/) 制作简历，关于如何写好简历，推荐学习鱼皮的 [保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)。

![老鱼简历网站简历模板大全](https://pic.yupi.icu/1/%E8%80%81%E9%B1%BC%E7%AE%80%E5%8E%86%E7%BD%91%E7%AB%99%E7%AE%80%E5%8E%86%E6%A8%A1%E6%9D%BF%E5%A4%A7%E5%85%A8.png)

3）面试题要理解而不是死记硬背：PostgreSQL 面试题和 MySQL 类似，但会更侧重高级特性（如窗口函数、CTE、JSON 操作、全文搜索等）。建议先理解原理，再去看面试题。

4）对比 PostgreSQL 和 MySQL：面试时经常会被问到 PostgreSQL 和 MySQL 的区别，要能够说出 PostgreSQL 的优势（如更强的 ACID 保证、丰富的数据类型、强大的扩展性等）。

6）多多参与面试：可以使用鱼皮的 [1 对 1 模拟面试](https://ai.mianshiya.com/) 来模拟真实的面试场景，消除真实面试时的紧张感。

![面多多 1 对 1 模拟面试网站](https://pic.yupi.icu/1/%E9%9D%A2%E5%A4%9A%E5%A4%9A%201%20%E5%AF%B9%201%20%E6%A8%A1%E6%8B%9F%E9%9D%A2%E8%AF%95%E7%BD%91%E7%AB%99.png)

更多求职干货：[编程导航求职干货分享](https://www.codefather.cn/learn?category=76&type=2)



### 经典面试题

**基础理论：**

1. PostgreSQL 和 MySQL 有什么区别？各有什么优缺点？
2. PostgreSQL 支持哪些数据类型？JSONB 和 JSON 有什么区别？
3. 什么是 MVCC？PostgreSQL 是如何实现 MVCC 的？
4. PostgreSQL 的事务隔离级别有哪些？默认是什么级别？

**SQL 相关：**

1. 什么是窗口函数？如何使用？
2. 什么是 CTE？CTE 和子查询有什么区别？
3. PostgreSQL 的 LEFT JOIN 和 RIGHT JOIN 有什么区别？
4. 如何编写递归查询？

**索引相关：**

1. PostgreSQL 支持哪些索引类型？各有什么特点？
2. GIN 索引和 B-Tree 索引有什么区别？
3. 什么是部分索引？什么时候使用部分索引？
4. 如何优化 PostgreSQL 的索引？

**高级特性：**

1. PostgreSQL 如何进行全文搜索？
2. PostgreSQL 的 JSONB 如何查询和索引？
3. PostgreSQL 的数组类型如何使用？
4. PostgreSQL 的存储过程和函数有什么区别？

**性能优化：**

1. 如何使用 EXPLAIN 分析 SQL 执行计划？
2. PostgreSQL 的慢查询如何优化？
3. 什么是 VACUUM？为什么需要 VACUUM？
4. PostgreSQL 如何进行主从复制？



### 面试题库

- ⭐ [PostgreSQL 数据库面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070255982329858)
- [SQL 基础查询面试题 - 面试鸭](https://www.mianshiya.com/bank/1813047253254930434)
- [SQL 进阶查询面试题 - 面试鸭](https://www.mianshiya.com/bank/1813047306893705217)
- [数据库性能优化面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070982775521282)
- [数据库高可用面试题 - 面试鸭](https://www.mianshiya.com/bank/1812070552811208705)
- [DBA 数据库运维面试题 - 面试鸭](https://www.mianshiya.com/bank/1811358412353388545)



### 求职资源

- ⭐ [鱼皮的保姆级求职指南](https://www.codefather.cn/course/job)：从简历到面试的完整指南
- ⭐ [鱼皮的保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)：如何写出高质量简历
- [编程导航的求职干货分享](https://www.codefather.cn/learn?category=76&type=2)：求职经验和技巧
- [老鱼简历](https://www.laoyujianli.com/)：写简历工具 + 简历模板大全
- [真实面经大全](https://www.codefather.cn/job/experience)：了解真实的面试流程
- [几百场真实面试视频](https://www.codefather.cn/mockInterview)：观看他人的面试过程
- [1 对 1 模拟面试](https://ai.mianshiya.com/)：AI 模拟面试练习
- [面试题讲解视频](https://space.bilibili.com/3546383483144655)：面试鸭官方题解



## 持续学习资源

### 知识总结

- ⭐ [编程导航](https://www.codefather.cn/)：学习路线、项目教程、面试题、编程资源一站式平台

### PostgreSQL 专题资源

- [PostgreSQL Wiki](https://wiki.postgresql.org/)：PostgreSQL 百科全书
- [Planet PostgreSQL](https://planet.postgresql.org/)：PostgreSQL 博客聚合
- [PostgreSQL 邮件列表](https://www.postgresql.org/list/)：PostgreSQL 官方邮件列表

### 技术博客

- [Uber Engineering Blog](https://www.uber.com/blog/engineering/)：Uber PostgreSQL 实践
- [Netflix TechBlog](https://netflixtechblog.com/)：Netflix 数据库架构
- [Instagram Engineering](https://instagram-engineering.com/)：Instagram PostgreSQL 优化

### 扩展推荐

- ⭐ [pgvector](https://github.com/pgvector/pgvector)：向量数据库扩展，支持 AI 应用
- [PostGIS](https://postgis.net/)：地理空间数据扩展
- [TimescaleDB](https://www.timescale.com/)：时序数据库扩展
- [Citus](https://www.citusdata.com/)：分布式 PostgreSQL 扩展
- [zhparser](https://github.com/amutu/zhparser)：中文分词扩展



## 写在最后

PostgreSQL 是一款功能强大、高度可扩展的开源数据库，被誉为 "世界上最先进的开源数据库"。虽然 MySQL 的市场份额更大，但 PostgreSQL 在技术上有很多优势，特别是在数据完整性、复杂查询、高级特性等方面。

学习 PostgreSQL 不仅能让你在求职时更有竞争力，还能帮助你处理更复杂的业务场景。特别是在 AI 时代，PostgreSQL 的向量扩展（pgvector）使其成为 AI 应用的理想数据库，开发 RAG 什么的非常合适。

如果你已经学过 MySQL，那么学习 PostgreSQL 会更加轻松。两者的基础 SQL 语法是相通的，重点是学习 PostgreSQL 特有的高级特性。

希望这份学习路线能够帮助大家少走弯路，更高效地掌握 PostgreSQL。加油鱼友们~

![](https://pic.yupi.icu/1/%E9%B1%BC%E7%9A%AE%E6%84%9F%E8%B0%A2.png)




## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
