# 2026年最新Redis学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Redis 缓存求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1791375592078610434)

⭐️ 推荐观看 [鱼皮的 Redis 导学视频](https://www.bilibili.com/video/BV1a1sgzfE5d/)，快速了解 Redis 学习路线和关键知识。



## 开篇介绍

很多同学第一次接触 Redis 可能都是因为 "缓存"，也有很多同学误以为 Redis 就是缓存、只能做缓存。

事实上，**Redis 是知名的高性能内存键值（K/V）存储系统**，除了缓存之外，Redis 还可以用作配置存储、消息队列、分布式锁、分布式 Session 等。正因为 Redis 的高性能、通用性、易用性、功能强大，使得它成为了后端开发中必不可少的中间件。

**Redis 是什么？** 

Redis（Remote Dictionary Server）是一个开源的内存数据库，它支持多种数据结构（字符串、哈希、列表、集合、有序集合等），提供了丰富的功能和极高的性能。Redis 的读写速度非常快（单机可达 10 万 QPS 以上），被广泛应用于各种高并发场景。

**为什么要学 Redis？**

只要你的学习方向是后端开发，就必须要系统地学习 Redis。在实际工作中，Redis 几乎是每个后端项目的标配，用于提升系统性能、解决高并发问题。不仅要学会如何应用到项目中，还要学习它优秀的系统设计以及实现原理，可以开拓我们开发程序、解决问题的思路。

在 AI 时代，Redis 能够广泛应用于数据缓存、向量存储、实时计算、AI 模型推理加速等场景中。

![](https://pic.yupi.icu/1/image-20251128134324387.png)

学习前提： 建议先学完一套开发框架（比如 SSM、SpringBoot）再学习 Redis，这样能更好地理解 Redis 在实际项目中的应用场景。关于 Spring Boot 的详细学习，可以查看 [Spring Boot 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754528483389441)。前端同学暂时不需要学习 Redis。



### 学习路线图

![Redis 缓存学习路线思维导图](https://pic.yupi.icu/roadmap/redis-cache-roadmap.png)

### 就业方向

掌握 Redis 可以帮助你胜任以下岗位（注意，这些岗位都需要掌握更多的技能，Redis 只是其中一部分）：

1. Java/Go/Python 后端开发工程师：后端开发的核心技能之一，几乎所有互联网公司都会用到 Redis。
2. 架构师：需要深入理解 Redis 的原理和最佳实践，能够设计高性能、高可用的缓存架构。



## 整体学习建议

1）先会用再深入原理：Redis 是一个注重实际运用的技术，建议先学会如何使用 Redis（安装、基本命令、数据结构），然后在项目中实践，最后再去深入学习底层原理。不要一上来就研究源码，容易打击学习积极性。

2）多敲命令多写代码：Redis 的命令很多，但不要死记硬背。跟着教程把常用命令都敲一遍，忘了就查文档或问 AI。建议多写代码，把 Redis 实际应用到你的项目中（比如加个缓存、实现个分布式锁），印象会更深刻。

3）重点学习缓存设计：缓存是 Redis 最核心的应用场景，一定要重点学习缓存的设计模式、缓存问题（穿透、击穿、雪崩）及其解决方案。这是面试的高频考点，也是实际工作中最常遇到的问题。

4）学完实战部分就够用了：对于初学后端的同学来说，学完第 2 阶段（实战应用）就足以应对大部分项目需求了。等主流的后端技术都会用后、面试前再回过头来补高级知识和原理即可。

5）善用 AI 辅助学习：遇到不懂的概念（比如什么是 RDB、AOF、主从复制），可以问 AI 工具（如 ChatGPT、Kimi），让它用通俗的语言解释给你听，或者让它帮你生成 Redis 命令。更多 AI 工具推荐可以看 [AI 导航](https://ai.codefather.cn/)。

![AI资源大全网站](https://pic.yupi.icu/1/AI%E8%B5%84%E6%BA%90%E5%A4%A7%E5%85%A8%E7%BD%91%E7%AB%99.png)

6）整理最佳实践笔记：Redis 的最佳实践部分（键值设计、客户端优化、服务端优化等）要重点学习，建议整理笔记便于自己复习查阅。争取养成好的设计和编码习惯，对以后的工作发展会很有帮助。




## 阶段 1：Redis 基础入门（3-7 天，仅供参考）

这个阶段主要学习 Redis 的基本概念、安装配置、数据结构和常用命令。学完这个阶段，你就能在项目中使用 Redis 进行简单的数据存储和查询了。

学习目标：理解 Redis 的基本概念和特点，掌握 Redis 的安装和基本操作，熟练使用 Redis 的五种基本数据结构，能够通过 Java/Python 等编程语言操作 Redis。



### 知识点

**Redis 基础概念【必学】：**

- Redis 是什么？
- SQL 和 NoSQL 的区别
- Redis 的特点（高性能、支持多种数据结构、持久化、主从复制等）
- Redis 的应用场景（缓存、Session 存储、消息队列、分布式锁等）

**Redis 安装和配置【必学】：**

- Redis 安装（Windows、Linux、Mac、Docker）
- Redis 启动和连接
- Redis 配置文件（redis.conf）
- Redis 常用命令行操作

**Redis 基本数据结构【必学，面试重点】：**

- String（字符串）：最基本的数据类型，可以存储字符串、数字、二进制数据
  - 常用命令：SET、GET、INCR、DECR、APPEND 等
- Hash（哈希）：类似于 Java 的 HashMap，适合存储对象
  - 常用命令：HSET、HGET、HMSET、HMGET、HGETALL 等
- List（列表）：有序的字符串列表，可以从两端插入和弹出元素
  - 常用命令：LPUSH、RPUSH、LPOP、RPOP、LRANGE 等
- Set（集合）：无序的字符串集合，元素不重复
  - 常用命令：SADD、SMEMBERS、SISMEMBER、SINTER、SUNION 等
- SortedSet（有序集合）：有序的字符串集合，每个元素关联一个分数（score）
  - 常用命令：ZADD、ZRANGE、ZRANGEBYSCORE、ZREM 等

**Redis 通用命令【必学】：**

- 键操作：KEYS、EXISTS、DEL、EXPIRE、TTL 等
- 数据库切换：SELECT
- 清空数据库：FLUSHDB、FLUSHALL

**Redis 客户端【必学】：**

- **Java 客户端**：
  - Jedis【建议学】
  - Spring Data Redis（RedisTemplate）【必学】
  - Lettuce【建议学】
  - Redisson【建议学，分布式场景必备】
- **Python 客户端**：
  - redis-py【必学】
- **Go 客户端**：
  - go-redis【必学】

**Redis 可视化工具【建议学】：**

- Redis Insight（官方推荐）
- Quick Redis
- RedisDesktopManager（RESP）



### 学习建议

1）Redis 基础入门要动手实践，不要只看不做。建议在本地安装 Redis，跟着教程把每个命令都敲一遍。

2）五种基本数据结构是 Redis 的核心，一定要理解每种数据结构的特点和使用场景。面试经常会问"什么场景用什么数据结构"。

3）Redis 客户端的选择：Java 推荐 Spring Data Redis（RedisTemplate），Python 推荐 redis-py，Go 推荐 go-redis。

4）建议学完基础后，立即在自己的项目中使用 Redis，比如给查询结果加个缓存，效果立竿见影。

### 学习资源

**Redis 入门教程：**

- ⭐ [黑马程序员 Redis 入门到实战教程](https://www.bilibili.com/video/BV1cr4y1671t)：深度透析 Redis 底层原理 + Redis 分布式锁 + 企业解决方案，强烈推荐

**官方文档：**

- [Redis 官方文档](https://redis.io/docs/)：权威参考资料（英文）
- [Redis 中文文档](http://www.redis.cn/)：中文版文档，方便查阅
- [Redis 命令参考](https://redis.io/commands/)：命令忘了就查

**鱼皮原创项目（入门实战 Redis）：**

- ⭐ [伙伴匹配系统](https://www.codefather.cn/course/1790950013153095682)：适合新手，学习 Redis 基础应用、事务、并发编程



## 阶段 2：Redis 实战应用（10-20 天，仅供参考）

这个阶段主要学习 Redis 在实际项目中的常见应用场景，包括缓存设计、分布式锁、消息队列、高级数据结构等。这是 Redis 学习的核心部分，也是面试的重点。

学习目标：掌握 Redis 的实战应用技巧，能够使用 Redis 解决实际项目中的缓存、分布式锁、消息队列等问题，理解并能解决常见的缓存问题（穿透、击穿、雪崩）。



### 知识点

**缓存设计【必学，面试重点】：**

- **缓存更新策略**：
  - Cache Aside（旁路缓存）模式【必学】
  - Read Through / Write Through 模式【建议学】
  - Write Behind（异步缓存写入）模式【建议学】
- **缓存问题及解决方案【必学，面试高频】**：
  - **缓存穿透**：查询不存在的数据，导致请求直接打到数据库
    - 解决方案：布隆过滤器、缓存空对象、参数校验
  - **缓存击穿**：热点数据过期，大量请求同时访问数据库
    - 解决方案：互斥锁、逻辑过期、热点数据永不过期
  - **缓存雪崩**：大量缓存同时过期，导致数据库压力剧增
    - 解决方案：过期时间加随机值、多级缓存、服务降级
- 缓存预热【建议学】
- 多级缓存架构【建议学】

**分布式应用【必学】：**

- 分布式 Session 存储
- 分布式全局唯一 ID 生成（雪花算法、Redis INCR）
- **分布式锁【必学，面试重点】**：
  - Redis 分布式锁的实现原理（SET NX EX）
  - Redisson 分布式锁的使用
  - 分布式锁的问题（死锁、锁超时等）及解决方案
  - 红锁（RedLock）【可不学】

**高级数据结构【建议学】：**

- **GEO（地理位置）**：附近的人、附近的店铺
- **Bitmap（位图）**：签到统计、在线用户统计
- **HyperLogLog**：UV 统计（去重计数）
- **Bloom Filter（布隆过滤器）**：缓存穿透解决方案

**Redis 消息队列【建议学】：**

- Pub/Sub（发布订阅）【建议学】
- Stream（流）【建议学】
- List 实现简单消息队列【建议学】

**Lua 脚本【建议学】：**

- Lua 脚本的作用（原子性操作）
- 在 Redis 中使用 Lua 脚本
- 使用 Lua 实现秒杀、限流等功能

**其他实战应用【建议学】：**

- Feed 流实现（关注推送）
- Redis 事务（MULTI、EXEC）
- 点赞、收藏、关注功能实现



### 学习建议

1）缓存设计是 Redis 最核心的应用，一定要重点学习。缓存三大问题（穿透、击穿、雪崩）是面试必考，要理解问题的本质和解决方案。

2）分布式锁是 Redis 的重要应用场景，建议通过实战项目学习。鱼皮的多个项目都实战了 Redis 分布式锁，比如 AI 答题应用平台。

3）Redisson 是 Redis 的高级客户端，封装了很多分布式场景的解决方案（分布式锁、分布式集合等）。建议重点学习 Redisson。

4）多级缓存架构（Redis + Caffeine 本地缓存）是企业级应用的标配，可以大幅提升系统性能。鱼皮的智能协同云图库和智能面试刷题平台都实战了多级缓存。

5）强烈建议通过鱼皮的实战项目学习 Redis 应用，从简单到复杂，从单机到集群，系统掌握 Redis 技术。

### 学习资源

**Redis 实战教程：**

- ⭐ [黑马程序员 Redis 入门到实战教程](https://www.bilibili.com/video/BV1cr4y1671t)：包含完整的实战项目，强烈推荐
- [编程导航 Redis 项目推荐及笔记](https://www.codefather.cn/post/1617711652306694146)：Redis 实战项目合集

**鱼皮原创项目（实战 Redis 应用）：**

新手入门（学习 Redis 基础应用）：
- ⭐ [伙伴匹配系统](https://www.codefather.cn/course/1790950013153095682)：Redis 基础应用、事务、并发编程、大数据推荐思想

企业级项目（学习 Redis 高级应用）：
- ⭐ [智能协同云图库](https://www.codefather.cn/course/1864210260732116994)：Redis + Caffeine 多级缓存、分布式锁、实时协同
- ⭐ [智能面试刷题平台](https://www.codefather.cn/course/1826803928691945473)：Redis 多级缓存、Redisson 高级数据结构、HotKey 探测、Sentinel 流控
- ⭐ [AI 答题应用平台](https://www.codefather.cn/course/1790274408835506178)：分布式锁、缓存设计、幂等设计、分库分表
- [亿级流量点赞系统](https://www.codefather.cn/course/1912696290659577857)：高并发 Redis 架构设计、高性能优化、分布式锁

更多项目：[鱼皮项目学习建议（必读）](https://www.codefather.cn/post/1797431216467001345)



## 阶段 3：Redis 高级特性和原理（10-15 天，仅供参考）

这个阶段主要学习 Redis 的高级特性（持久化、主从复制、哨兵、集群）和底层原理（数据结构、网络模型、内存淘汰策略等）。

💡 注意，这部分内容相对较深，对于初学者来说，可以在学完前 2 个阶段后先去学习其他后端技术（消息队列、微服务等），等面试前再回过头来补这部分内容。

学习目标：理解 Redis 的持久化机制、主从复制原理、哨兵和集群的工作原理，了解 Redis 的底层数据结构和网络模型，能够设计高可用的 Redis 架构。



### 知识点

**Redis 最佳实践【必学】：**

- 键值设计规范（命名规范、避免大 Key、选择合适的数据结构）
- 客户端优化（连接池配置、批量操作、Pipeline）
- 服务端优化（内存优化、慢查询分析）

**Redis 持久化【必学，面试重点】：**

- **RDB（快照）**：定时将内存中的数据快照保存到磁盘
- **AOF（追加文件）**：记录每个写操作，重启时重新执行
- RDB 和 AOF 的对比及选择【必学】
- 混合持久化（RDB + AOF）【建议学】

**Redis 主从复制【必学，面试重点】：**

- 主从复制的原理和作用
- 全量同步和增量同步
- 主从集群的优化（复制积压缓冲区、复制偏移量）
- 主从切换【建议学】

**Redis 哨兵（Sentinel）【建议学，面试重点】：**

- 哨兵的作用（监控、自动故障转移）
- 哨兵集群的工作原理
- 故障转移流程

**Redis 分片集群（Cluster）【建议学，面试重点】：**

- 分片集群的作用（数据分片、高可用）
- 散列插槽（Hash Slot）
- 集群伸缩（添加/删除节点）
- 故障转移和数据迁移

**Redis 底层数据结构【建议学】：**

- 动态字符串（SDS）
- 整数集合（IntSet）
- 字典（Dict）
- 压缩列表（ZipList）
- 跳表（SkipList）
- Redis 对象（RedisObject）
- 五种基本数据结构的底层实现

**Redis 网络模型【建议学】：**

- 阻塞 I/O
- 非阻塞 I/O
- I/O 多路复用（epoll）
- Redis 的单线程模型【面试重点】
- Redis 6.0 的多线程【建议学】

**其他高级知识【可不学】：**

- Redis 通信协议（RESP）
- Redis 内存淘汰策略（LRU、LFU 等）【面试重点】
- Redis 过期策略（定时删除、惰性删除、定期删除）【面试重点】



### 学习资源

- ⭐ [黑马程序员 Redis 入门到实战教程](https://www.bilibili.com/video/BV1cr4y1671t)：包含高级知识和原理讲解
- [10 小时彻底掌握 Redis](https://www.bilibili.com/video/BV1tP411t7Uo)：Redis 最新全套视频教程，从原理到实战



## 阶段 4：Redis 求职备战（面试前 1 个月突击）

这个阶段主要是为求职做准备，包括准备简历、刷面试题、模拟面试等。

鱼皮个人建议：不到面试前一个月，不要急着背面试题！应该先把前 3 个阶段的知识学扎实，有了实际的项目经验后，再来突击面试题效果会更好。当然，提前看看题目来巩固知识是 OK 的。

学习目标：熟练掌握 Redis 常见面试题，能够流畅回答面试官的提问，准备好简历上的 Redis 相关项目经历，顺利通过 Redis 相关的面试。



### 学习建议

1）提前做规划：尽早做规划，建议至少提前 3 个月开始准备。推荐阅读鱼皮的 [保姆级求职指南](https://www.codefather.cn/course/job)，从求职规划到拿 Offer 全流程讲解。

2）打磨简历：简历上一定要有能体现你 Redis 能力的项目经历。比如你用 Redis 做了什么功能、解决了什么性能问题、如何设计缓存方案等。建议使用 [老鱼简历](https://www.laoyujianli.com/) 制作简历，关于如何写好简历，推荐学习鱼皮的 [保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)。

![老鱼简历网站简历模板大全](https://pic.yupi.icu/1/%E8%80%81%E9%B1%BC%E7%AE%80%E5%8E%86%E7%BD%91%E7%AB%99%E7%AE%80%E5%8E%86%E6%A8%A1%E6%9D%BF%E5%A4%A7%E5%85%A8.png)

3）面试题要理解而不是死记硬背：Redis 面试题很多，但是核心考点就那么些（缓存设计、分布式锁、持久化、集群等）。建议先理解原理，再去看面试题，这样即使遇到没见过的题目，也能答出个大概。建议使用 [面试鸭](https://www.mianshiya.com/) 刷题，支持按公司、题型、难度筛选。

![面试鸭刷题神器热门八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8%E7%83%AD%E9%97%A8%E5%85%AB%E8%82%A1%E6%96%87.png)

4）多看真实面经：建议去看看 [真实面经大全](https://www.codefather.cn/job/experience)，了解不同公司的面试风格和常考题目。也可以看看 [几百场真实面试视频](https://www.codefather.cn/mockInterview)，提前做好准备。

5）善用 AI 模拟面试：可以使用 [AI 模拟面试](https://ai.mianshiya.com/) 来模拟真实的面试场景，提前练习如何回答 Redis 相关的面试题。

![面多多 1 对 1 模拟面试网站](https://pic.yupi.icu/1/%E9%9D%A2%E5%A4%9A%E5%A4%9A%201%20%E5%AF%B9%201%20%E6%A8%A1%E6%8B%9F%E9%9D%A2%E8%AF%95%E7%BD%91%E7%AB%99.png)

6）看面试题讲解视频：推荐关注 [面试鸭 B 站账号](https://space.bilibili.com/3546383483144655)，里面有很多 Redis 面试题的详细讲解。

更多求职干货：[编程导航求职干货分享](https://www.codefather.cn/learn?category=76&type=2)



### 经典面试题

下面列举一些 Redis 相关的高频考题，供大家参考：

**基础理论：**

1. Redis 是什么？有哪些特点？
2. Redis 为什么这么快？
3. Redis 和 Memcached 的区别？
4. Redis 的数据类型有哪些？分别有什么应用场景？
5. Redis 的单线程模型是什么？为什么单线程还这么快？

**缓存相关：**

1. 什么是缓存穿透、缓存击穿、缓存雪崩？如何解决？【高频】
2. Redis 的缓存更新策略有哪些？
3. 如何保证缓存和数据库的数据一致性？
4. 什么是热点 Key 问题？如何解决？
5. 什么是大 Key 问题？如何解决？

**分布式锁：**

1. 如何用 Redis 实现分布式锁？【高频】
2. Redis 分布式锁有哪些问题？如何解决？
3. Redisson 是如何实现分布式锁的？
4. 什么是红锁（RedLock）？

**持久化和高可用：**

1. Redis 的持久化方式有哪些？RDB 和 AOF 的区别？【高频】
2. Redis 的主从复制是如何实现的？
3. Redis 哨兵的作用是什么？故障转移流程是怎样的？
4. Redis 集群（Cluster）是如何实现的？
5. Redis 集群的数据分片策略是什么？

**底层原理：**

1. Redis 的过期策略有哪些？【高频】
2. Redis 的内存淘汰策略有哪些？【高频】
3. Redis 的底层数据结构是什么？（跳表、压缩列表等）
4. Redis 的 I/O 多路复用是什么？



### 面试题库

- ⭐ [Redis 面试题 - 面试鸭](https://www.mianshiya.com/bank/1791375592078610434)



加油小伙伴们 💪🏻！

![](https://pic.yupi.icu/1/xiongmaotouthumb.jpeg)



## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
