# 2026年最新SpringCloud微服务学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Spring Cloud 求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1797453053310402561)



## 开篇介绍

Spring Cloud 是基于 Spring Boot 的微服务开发框架，提供了服务注册和发现、配置管理、服务网关、负载均衡、熔断器、分布式追踪等微服务架构所需的核心组件。Spring Cloud 是目前 Java 微服务架构的首选技术栈，被广泛应用于互联网公司和传统企业。

**为什么要学 Spring Cloud？**

首先，微服务是目前企业级应用的主流架构，几乎所有互联网公司都在使用微服务架构。掌握 Spring Cloud 是 Java 后端工程师的必备技能，也是进入大厂的敲门砖。

其次，微服务架构师的薪资非常高，一线城市的资深 Java 工程师平均薪资在 25-50K，架构师可以达到 50-100K+。

最后，Spring Cloud Alibaba 是阿里巴巴开源的 Spring Cloud 实现，提供了 Nacos、Sentinel、Seata 等组件，在国内应用非常广泛。在云原生时代，Spring Cloud 也在积极拥抱 Kubernetes、Docker、Service Mesh 等技术。关于云原生开发的详细学习，可以查看 [云原生开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755030541578242)。



### 学习前提

学习 Spring Cloud 建议先掌握：
1. Java 基础：熟练掌握 Java 编程
2. Spring Boot：熟练使用 Spring Boot 开发 Web 应用【必学】
3. MySQL：理解关系型数据库【建议】
4. Redis：理解缓存【建议】

关于 Spring Boot 的详细学习，可以查看 [Spring Boot 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754528483389441)



### 学习路线图

![Spring Cloud 微服务学习路线思维导图](https://pic.yupi.icu/roadmap/spring-cloud-microservices-roadmap.png)

### 就业方向

学完 Spring Cloud 后，能帮你增加以下岗位的竞争力：

1. Java 后端工程师：使用 Spring Cloud 开发微服务应用
2. 微服务架构师：设计和实施微服务架构
3. 全栈工程师：Spring Cloud 后端 + 前端
4. DevOps 工程师：微服务的部署和运维



## 整体学习建议

1）Spring Cloud 是基于 Spring Boot 的，**一定要先熟练掌握 Spring Boot**，再学习 Spring Cloud。Spring Boot 的核心概念，比如自动配置、Starter、Actuator 等，在 Spring Cloud 中同样适用。

2）学习 Spring Cloud 之前，要先理解微服务架构的核心思想和设计原则。理解微服务的优缺点，理解为什么要拆分服务、什么时候该拆分、什么时候不该拆分。

![](https://pic.yupi.icu/1/image-20251203095455089.png)

3）优先学 Spring Cloud Alibaba：传统的 Spring Cloud Netflix（Eureka、Hystrix 等）已经进入维护模式，国内大厂推出的 Spring Cloud Alibaba（Nacos、Sentinel 等）是更好的选择，Spring Cloud Alibaba 的中文文档更完善，更适合国内开发者。

4）微服务架构的学习一定要结合实际项目。**建议搭建一个完整的微服务项目**，体验服务拆分、服务通信、服务治理等过程，否则很难真正理解微服务。

鱼皮大学时就是做了个微服务的面试刷题 APP，顺便去参加了个竞赛还拿了奖嘿嘿~

![](https://pic.yupi.icu/1/v2-f8b76581b3bdea715e5f2a8fa2f2b1c3_1440w.jpg)



## 阶段 1：微服务架构基础（3-15 天，仅供参考）

### 学习目标

理解微服务架构的核心概念，掌握系统架构的演进过程。



### 知识点

**系统架构演进【必学】：**

- 单体架构
- 垂直架构
- SOA 架构
- 微服务架构

**微服务架构【必学】：**

- 微服务的定义和特点
- 微服务的优缺点
- 微服务的设计原则
- 服务拆分策略

**Spring Boot 基础【必学】：**

- Spring Boot 的核心概念
- Spring Boot 自动配置
- Spring Boot Starter
- RESTful API 开发



### 学习重点

1）Spring Boot 是 Spring Cloud 的基础，要熟练掌握 Spring Boot 的使用。包括如何创建 Spring Boot 项目、如何开发 RESTful API、如何集成数据库等。

2）微服务不是银弹，有优点也有缺点。优点是可维护性、可扩展性、容错性好；缺点是分布式系统的复杂性、数据一致性、运维难度等。要理解微服务的适用场景。

3）服务拆分要遵循单一职责原则，一个服务只做一件事。服务的粒度不宜过大（变成小单体）也不宜过小（过度拆分）。



### 学习资源

- [Spring Boot 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754528483389441)：完整的 Spring Boot 学习路线



## 阶段 2：服务注册和发现（7-15 天，仅供参考）

服务注册中心是微服务架构的核心组件，所有服务都要注册到注册中心。服务之间的调用通过注册中心获取服务地址。

Nacos 是阿里巴巴开源的服务注册中心，功能强大，支持服务注册、配置管理、DNS 等。如今，Nacos 是目前国内最流行的服务注册中心，强烈推荐学习。

![](https://pic.yupi.icu/1/image-20251203101010617.png)



### 学习目标

掌握服务注册和发现机制，能够使用 Nacos 实现服务注册。



### 知识点

**服务注册和发现【必学】：**

- 服务注册和发现的原理
- CAP 理论（一致性、可用性、分区容错性）
- AP 和 CP 的区别

**Nacos【必学，推荐】：**

- Nacos 的安装和启动
- 服务注册和发现
- Nacos 的控制台

**Eureka【建议学】：**

- Eureka 的原理
- Eureka Server 和 Eureka Client
- Eureka 的自我保护机制【可不学】

**Consul【可不学】：**

- Consul 的基本使用



### 学习建议

1）Eureka 是 Netflix 开源的服务注册中心，Spring Cloud 早期的默认选择。但 Eureka 已经进入维护模式，不再更新。了解即可，实际项目建议使用 Nacos。

2）CAP 理论是分布式系统的重要理论，要理解一致性、可用性、分区容错性的含义和权衡。

![](https://pic.yupi.icu/1/1_Br1FrvKnK3hU6Xl_LbDkwg.webp)



### 学习资源

- ⭐ [Spring Cloud Alibaba 教程](https://www.bilibili.com/video/BV14XxNzkEAB/)：2025 最新，92 集完整教程
- [Spring Cloud 微服务教程](https://www.bilibili.com/video/BV1Fi4y1s7up/)：2024 入门到精通
- [Nacos 官方文档](https://nacos.io/zh-cn/docs/what-is-nacos.html)：官方文档



## 阶段 3：服务调用和负载均衡（2-5 天，仅供参考）

### 学习目标

掌握服务间调用方法，能够实现负载均衡。



### 知识点

**负载均衡【必学】：**

- 负载均衡的原理
- 客户端负载均衡 vs 服务端负载均衡
- 负载均衡算法（轮询、随机、加权等）

**LoadBalancer【必学，推荐】：**

- Spring Cloud LoadBalancer 的使用
- 自定义负载均衡策略

**Ribbon【建议学】：**

- Ribbon 的原理
- Ribbon 的负载均衡策略

**服务调用【必学】：**

- RestTemplate
- OpenFeign【重点】
- Feign 的声明式调用



### 学习建议

1）LoadBalancer 是 Spring Cloud 官方推荐的负载均衡组件，取代了 Ribbon。LoadBalancer 更轻量、更简单。

2）OpenFeign 是声明式的 HTTP 客户端，可以像调用本地方法一样调用远程服务。OpenFeign 是微服务间调用的首选方式，一定要熟练掌握。

3）Ribbon 是 Netflix 开源的客户端负载均衡器，已经进入维护模式。了解即可，实际项目建议使用 LoadBalancer。



### 学习资源

- [OpenFeign 官方文档](https://docs.spring.io/spring-cloud-openfeign/docs/current/reference/html/)：官方文档



## 阶段 4：API 网关（3-7 天，仅供参考）

API 网关是微服务架构的入口，所有请求都要经过网关。网关可以实现路由转发、负载均衡、限流、鉴权、日志等功能。

![](https://pic.yupi.icu/1/image-20251203100409806.png)

Spring Cloud Gateway 是 Spring Cloud 官方推荐的网关组件，基于 WebFlux 实现，性能优秀。Gateway 已经取代了 Zuul 成为主流网关。



### 学习目标

掌握 API 网关的使用，能够使用 Gateway 实现路由和过滤。



### 知识点

**API 网关【必学】：**

- API 网关的作用
- 网关的核心功能（路由、过滤、限流等）

**Spring Cloud Gateway【必学】：**

- Gateway 的基本使用
- 路由配置（Route）
- 断言（Predicate）
- 过滤器（Filter）
- 全局过滤器和局部过滤器
- 网关整合 Nacos【重点】

**限流和鉴权【建议学】：**

- 网关限流
- 网关鉴权



### 学习建议

1）Gateway 的三大核心概念是 Route（路由）、Predicate（断言）、Filter（过滤器）。要理解这三个概念的作用和配置方法。

2）网关可以整合 Nacos 实现动态路由，根据服务注册信息自动生成路由规则。

3）AI 时代，推出了一类新的技术 —— AI 网关，给网关赋予了更强大的能力，比如 AI 大模型统一调用、统一监控大模型调用情况等等。可以看 [鱼皮的 AI 网关讲解视频](https://www.bilibili.com/video/BV14NyrBTEeB/) 快速了解。




## 阶段 5：服务治理（3-10 天，仅供参考）

服务熔断和降级是微服务架构的重要保护机制。当某个服务出现故障时，熔断器可以快速失败，避免级联故障。降级可以返回兜底数据，保证系统部分可用。



### 学习目标

掌握服务治理的核心技术，能够实现服务熔断、配置管理、分布式事务等。



### 知识点

**服务熔断和降级【必学】：**

- 熔断器的原理
- Sentinel【重点，推荐】
- Hystrix【建议学】
- 降级策略

**配置中心【必学】：**

- 配置中心的作用
- Nacos Config【推荐】
- Spring Cloud Config【建议学】
- 配置的动态刷新

**分布式事务【建议学】：**

- 分布式事务的问题
- Seata 的使用
- AT、TCC、Saga 模式【可不学】

**分布式追踪【建议学】：**

- 分布式追踪的作用
- Sleuth + Zipkin
- SkyWalking【可不学】



### 学习建议

1）Sentinel 是阿里巴巴开源的熔断限流组件，功能强大，支持熔断、降级、限流、热点参数限流等。Sentinel 也是目前最流行的服务保护组件，强烈推荐学习。

![](https://pic.yupi.icu/1/90383206-674b2580-e0b2-11ea-83ca-d3e4934a8c6d.png)

2）配置中心可以统一管理所有服务的配置，支持配置的动态刷新。Nacos 既可以做服务注册中心，也可以做配置中心，非常方便。

3）分布式事务是微服务架构的难点，Seata 提供了分布式事务的解决方案。但分布式事务的使用场景有限，了解即可。

4）想要实战 Sentinel 和 Nacos 的同学，推荐学习鱼皮的 [智能面试刷题平台](https://www.codefather.cn/course/1813803565348171777)，项目中详细讲解了 Sentinel 熔断限流和 Nacos 配置中心的实战应用。



### 学习资源

- ⭐ [智能面试刷题平台](https://www.codefather.cn/course/1813803565348171777)：实战 Sentinel + Nacos
- [Sentinel 官方文档](https://sentinelguard.io/zh-cn/)：官方文档
- [Seata 官方文档](https://seata.io/zh-cn/docs/overview/what-is-seata.html)：官方文档



## 阶段 6：项目实战（30-60 天，仅供参考）

### 学习目标

通过实际项目巩固所学知识，积累微服务项目经验。



### 学习建议

1）搭建完整的微服务项目：从零开始搭建一个完整的微服务项目，包括服务注册、服务调用、网关、熔断、配置管理等。

2）服务拆分实践：将一个单体应用拆分成多个微服务，体验服务拆分的过程和挑战。

3）学习优秀开源项目：GitHub 上有很多优秀的 Spring Cloud 开源项目，可以学习其架构设计和实现细节。

4）关注性能和监控：微服务架构的性能和监控非常重要，要学习如何优化微服务性能、如何监控微服务健康状态。



### 项目推荐

**鱼皮原创项目（Spring Cloud 微服务架构，强烈推荐）：**

- ⭐ [API 开放平台](https://www.codefather.cn/course/1790979723916521474)：实战 Dubbo + Gateway 微服务架构，学习 API 签名认证、SDK 开发、网关鉴权等
- ⭐ [OJ 判题系统](https://www.codefather.cn/course/1790988592182808578)：实战 Spring Cloud 微服务架构，学习代码沙箱、消息队列、服务拆分等
- ⭐ [AI 零代码应用生成平台（25年最新）](https://www.codefather.cn/course/1948291549923344386)：实战 Spring Cloud + Dubbo 微服务架构，学习 LangChain4j AI 智能体、LangGraph4j 工作流、多种设计模式、多维度系统优化
- [智能面试刷题平台](https://www.codefather.cn/course/1813803565348171777)：实战 Sentinel 熔断限流 + Nacos 配置中心

**其他项目方向：**

- 电商微服务（商品、订单、用户等服务）
- 博客系统微服务
- 在线教育平台微服务
- 完整的电商平台（包括秒杀、支付等复杂场景）



### 学习资源

- ⭐ [鱼皮原创微服务项目教程](https://www.codefather.cn/course/project)：保姆级项目教程
- [Spring Cloud 实战项目](https://www.bilibili.com/video/BV1i8x4etEsR/)：2024 入门到进阶



## 阶段 7：求职备战（面试前 1 个月突击）

### 学习目标

熟练掌握 Spring Cloud 常见面试题，准备好简历和项目经历，顺利通过面试。



### 学习建议

1）准备微服务项目：如果企业招聘要求上明确写了微服务，那么简历上一定要有微服务项目经历。面试时要能够清楚地介绍项目的服务拆分、技术选型、遇到的问题和解决方案等。推荐学习鱼皮的 [API 开放平台](https://www.codefather.cn/course/1790979723916521474)、[AI 零代码应用生成平台](https://www.codefather.cn/course/1948291549923344386) 等微服务项目。

2）准备简历：建议使用 [老鱼简历](https://www.laoyujianli.com/) 工具快速制作简历；关于如何写好简历，建议完整阅读鱼皮的 [保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)。

![老鱼简历网站简历模板大全](https://pic.yupi.icu/1/%E8%80%81%E9%B1%BC%E7%AE%80%E5%8E%86%E7%BD%91%E7%AB%99%E7%AE%80%E5%8E%86%E6%A8%A1%E6%9D%BF%E5%A4%A7%E5%85%A8.png)

3）多刷面试题：Spring Cloud 的面试题主要包括微服务架构、服务注册、服务调用、网关、熔断等，建议使用 [面试鸭](https://www.mianshiya.com/) 小程序或网页端刷题。

![面试鸭刷题神器java八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8java%E5%85%AB%E8%82%A1%E6%96%87.png)

4）微服务是分布式系统，要理解分布式系统的 CAP 理论、一致性问题、分布式锁等核心概念。这些都是架构师面试的必考内容。

更多求职干货：[编程导航求职干货分享](https://www.codefather.cn/learn?category=76&type=2)



### 经典面试题

**微服务架构：**

1. 什么是微服务？微服务有什么优缺点？
2. 如何拆分微服务？有什么原则？
3. 微服务和单体架构有什么区别？
4. 如何保证微服务的数据一致性？

**服务注册和发现：**

1. 服务注册和发现的原理是什么？
2. Nacos 和 Eureka 有什么区别？
3. CAP 理论是什么？
4. Nacos 如何实现服务健康检查？

**服务调用：**

1. OpenFeign 是什么？如何使用？
2. LoadBalancer 的负载均衡算法有哪些？
3. 如何自定义负载均衡策略？

**API 网关：**

1. API 网关的作用是什么？
2. Spring Cloud Gateway 的三大核心概念是什么？
3. Gateway 的过滤器有哪些类型？
4. 如何通过 Gateway 实现限流？

**服务治理：**

1. 什么是服务熔断？什么是服务降级？
2. Sentinel 和 Hystrix 有什么区别？
3. 如何使用 Nacos 作为配置中心？
4. 什么是分布式事务？Seata 的工作原理是什么？



### 面试题库

- ⭐ [Spring Cloud 面试题 - 面试鸭](https://www.mianshiya.com/bank/1797453053310402561)
- [微服务架构面试题 - 面试鸭](https://www.mianshiya.com/bank/1991433816622759937)
- [Java 微服务面试题](https://www.bilibili.com/video/BV1px4y1B7xN/)



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
- [Spring Boot 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754528483389441)：完整的 Spring Boot 学习路线
- [Java 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1789190431398928386)：完整的 Java 学习路线

### Spring Cloud 资源

- [Spring Cloud 官方文档](https://spring.io/projects/spring-cloud)：官方文档
- [Spring Cloud Alibaba 官方文档](https://sca.aliyun.com/)：官方文档
- [Nacos 官网](https://nacos.io/zh-cn/)：Nacos 官方网站
- [Sentinel 官网](https://sentinelguard.io/zh-cn/)：Sentinel 官方网站

### 技术博客

- [Spring 官方博客](https://spring.io/blog)：Spring Cloud 官方博客
- [Netflix TechBlog](https://netflixtechblog.com/)：Netflix 微服务架构先驱
- [Uber Engineering Blog](https://www.uber.com/blog/engineering/)：Uber 微服务实践
- [Airbnb Tech Blog](https://medium.com/airbnb-engineering)：Airbnb 服务化架构



## 总结一下

学习 Spring Cloud 要先打好 Spring Boot 基础，理解微服务架构的核心思想。从服务注册和发现开始，逐步学习服务调用、网关、熔断、配置管理等核心组件。多做实战项目，体验完整的微服务开发流程。

在云原生时代，Spring Cloud 结合 Kubernetes、Docker、Service Mesh 等技术，将为微服务架构带来更强大的能力。掌握 Spring Cloud，不仅能让你开发微服务应用，还能理解云原生架构的核心思想。

希望这份学习路线能够帮助大家少走弯路，更高效地掌握 Spring Cloud 微服务开发。

加油小伙伴们 💪🏻！

![](https://pic.yupi.icu/1/%E5%8A%A0%E6%B2%B9%E8%A1%A8%E6%83%85%E5%8C%85.jpeg)



## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
