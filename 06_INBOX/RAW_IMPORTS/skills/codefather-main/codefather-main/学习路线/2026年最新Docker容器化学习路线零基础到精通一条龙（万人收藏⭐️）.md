# 2026年最新Docker容器化学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Docker 求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1812067352871829505)



## 开篇介绍

Docker 是一个开源的容器化平台，它彻底改变了应用程序的打包、分发和运行方式。通过 Docker，你可以将应用程序及其所有依赖（库、配置文件、运行时环境）打包成一个轻量级、可移植的容器，确保在任何支持 Docker 的环境中都能完全一致地运行。这就像把你的应用装进了一个集装箱，无论运到哪里，都能原封不动地打开使用。

Docker 的图标就是这个意思：

![](https://pic.yupi.icu/1/images6.png)



### 为什么要学 Docker？

有句经典的程序员黑话 —— 在我的机器上能跑。Docker 就是来解决这个问题的。它保证了开发、测试、生产环境的完全一致性，再也不会出现 “我这边没问题啊” 的尴尬场景了。

Docker 的另一大优势是快速部署。传统的虚拟机需要几分钟甚至十几分钟才能启动，而 Docker 容器只需要几秒钟，这对于需要频繁部署和更新的应用来说，效率提升是巨大的。同时，Docker 容器比虚拟机更轻量，因为它们共享宿主机的内核，不需要每个容器都运行一个完整的操作系统，这意味着你可以在同一台机器上运行更多的应用。

易于扩展也是 Docker 的重要特性。当你的应用流量突然增大时，只需要几秒钟就可以启动多个容器实例，实现水平扩展。而当流量降低时，又可以快速关闭多余的容器，节省资源。

如今，Docker 已经成为互联网公司的标准配置。无论是 BAT 这样的大厂，还是鱼厂这样的创业公司，几乎都在使用 Docker 进行应用部署。对于微服务架构来说，Docker 更是不可或缺的基础设施，每个微服务都可以打包成一个独立的容器，独立部署、独立扩展。



### Docker 的应用场景

Docker 是一个基础工具技能，作为后端开发工程师，你可以使用 Docker 部署和管理自己的应用，让应用在任何环境下都能稳定运行。如果你是 DevOps 工程师，Docker 可以帮助你构建 CI/CD 流水线，实现自动化构建、测试和部署。

对于运维工程师来说，Docker + Kubernetes 的组合可以帮助你管理大规模的容器集群，实现自动化运维。

此外，全栈开发工程师也可以使用 Docker 快速搭建开发环境，一个 `docker-compose up` 命令就能启动整个应用栈（数据库、缓存、Web 服务器等），大大提高开发效率。

在云原生时代，Docker 更是必备技能。无论你是开发云原生应用，还是将传统应用容器化迁移到云上，都离不开 Docker。



### 学习前提

学习 Docker 之前，建议先掌握一些基础知识。

首先是 Linux 基础，因为 Docker 最初是在 Linux 上开发的，虽然现在也支持 Windows 和 macOS，但大部分生产环境都是 Linux。关于 Linux 的详细学习，可以查看 [Linux 服务器学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755996393320449)。你需要了解基本的 Linux 命令，比如 `cd`、`ls`、`cp`、`mv`、`rm`、`cat`、`grep` 等，关于 Shell 脚本的详细学习，可以查看 [Shell 脚本学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990756051800076290)。

其次是网络基础，你需要了解 IP 地址、端口、HTTP 协议等基本概念。Docker 容器之间的通信、容器与宿主机的通信，都涉及到网络知识。如果这些概念还不太熟悉，建议先补充一下网络基础。

最后，建议至少掌握一门编程语言（Java、Python、Node.js 等），因为你需要编写 Dockerfile 来构建自己的镜像。虽然 Dockerfile 的语法很简单，但如果你了解编程，会更容易理解镜像构建的过程。

💡 不过现在有 AI 了，很多 Dockerfile 不需要自己写，你可以把关注点放在镜像的构建流程上。



### 学习路线图

![Docker 容器化学习路线思维导图](https://pic.yupi.icu/roadmap/docker-containerization-roadmap.png)



## 整体学习建议

1）Docker 是一门实践性很强的技术，光看教程是不够的。建议在自己的电脑或云服务器上安装 Docker，跟着教程动手操作。

![](https://pic.yupi.icu/1/DD-hiroko.png)

2）先学会运行一个简单的容器（如 Nginx、MySQL），理解容器的基本概念，再逐步深入。

3）理解 Docker 和虚拟机的区别：Docker 容器不是虚拟机，它们的原理和应用场景不同。

4）Docker Hub 提供了大量官方镜像，建议优先使用官方镜像，它们经过充分测试，更加稳定可靠。

5）遇到 Docker 命令不熟悉、Dockerfile 不会写时，可以使用 AI 工具（比如 DeepSeek 等）辅助学习。推荐到鱼皮的 [编程导航 AI 资源大全](https://ai.codefather.cn/) 中找一找工具。

![AI资源大全网站](https://pic.yupi.icu/1/AI%E8%B5%84%E6%BA%90%E5%A4%A7%E5%85%A8%E7%BD%91%E7%AB%99.png)

6）关注安全性：Docker 容器的安全性很重要，尤其是在生产环境。建议学习 Docker 安全最佳实践，比如使用非 root 用户、限制容器权限等。

7）Kubernetes（K8s）是 Docker 之上的容器编排平台，功能强大但学习难度较大。建议先掌握 Docker，再学习 Kubernetes。

关于 Kubernetes 的详细学习，可以查看 [Kubernetes 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754993086443521)。



## 学习路线

### 阶段 1：Docker 基础

了解 Docker 的基本概念和核心组件。



#### 知识点

**Docker 基础概念：**

- 【必学】什么是容器化？容器 vs 虚拟机
- 【必学】Docker 的架构：Docker Engine、Docker Client、Docker Daemon
- 【必学】Docker 的核心概念：镜像（Image）、容器（Container）、仓库（Registry）
- 【必学】Docker 的优势和应用场景

**环境搭建：**

- 【必学】Docker 安装：Linux、Windows、macOS
- 【必学】Docker 国内镜像加速配置
- 【必学】验证安装：`docker version`、`docker info`

**基础命令：**

- 【必学】镜像管理：`docker pull`、`docker images`、`docker rmi`
- 【必学】容器管理：`docker run`、`docker ps`、`docker stop`、`docker rm`
- 【必学】容器交互：`docker exec`、`docker logs`、`docker inspect`
- 【建议学】容器资源限制：`--memory`、`--cpus`

**第一个 Docker 容器：**

- 【必学】运行 Hello World 容器
- 【必学】运行 Nginx 容器
- 【必学】运行 MySQL 容器



#### 学习重点

1）Docker 的核心概念（镜像、容器、仓库）一定要理解清楚，这是学习 Docker 的基础。简单来说：镜像是静态的模板，容器是镜像的运行实例，仓库是存储镜像的地方。

2）Docker 安装建议使用官方文档的方法，不要使用第三方脚本。Linux 系统推荐使用 Ubuntu 或 CentOS，Windows 和 macOS 推荐使用 Docker Desktop。

3）国内访问 Docker Hub 可能很慢，建议配置国内镜像加速（如阿里云、腾讯云等）。

4）`docker run` 命令非常重要，它集成了镜像下载、容器创建和启动的功能。建议重点学习它的参数，如 `-d`（后台运行）、`-p`（端口映射）、`-v`（数据卷挂载）等。

5）多动手练习：尝试运行不同的容器（Nginx、MySQL、Redis、MongoDB 等），理解容器的启动、停止、删除等操作。



#### 学习资源

- ⭐ [Docker 官方文档](https://docs.docker.com/)：最权威的学习资料
- ⭐ [2025 最新 Docker + K8S 完整教程](https://www.bilibili.com/video/BV1jTLizEEeo/)：49 集系统教程
- ⭐ [Docker 从入门到实践](https://yeasy.gitbook.io/docker_practice/)：GitHub 开源书籍，中文
- [尚硅谷 Docker 教程](https://www.bilibili.com/video/BV1gr4y1U7CY)：讲解详细，适合零基础
- [Docker 官方快速入门](https://docs.docker.com/get-started/)：官方教程
- [40 分钟的 Docker 实战教程](https://www.bilibili.com/video/BV1THKyzBER6)
- [尚硅谷 3 小时速通 Docker 教程](https://www.bilibili.com/video/BV1Zn4y1X7AZ)
- [黑马程序员 Docker 快速入门到项目部署](https://www.bilibili.com/video/BV1HP4118797)


#### 面试题库

- [Docker 基础面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067352871829505)



### 阶段 2：Docker 镜像和容器深入

深入学习 Docker 镜像和容器的原理和高级用法。

![](https://pic.yupi.icu/1/docker-containerized-appliction-blue-border_2.png)



#### 知识点

**镜像原理：**

- 【必学】镜像分层结构：UnionFS、写时复制（Copy-on-Write）
- 【必学】镜像标签（Tag）：版本管理
- 【必学】镜像仓库：Docker Hub、私有仓库
- 【建议学】镜像导入导出：`docker save`、`docker load`

**容器生命周期：**

- 【必学】容器的状态：Created、Running、Paused、Stopped、Exited
- 【必学】容器的启动、停止、重启、删除
- 【必学】容器的日志查看和调试
- 【建议学】容器的资源监控：`docker stats`

**容器和宿主机交互：**

- 【必学】端口映射：`-p`、`-P`
- 【必学】文件拷贝：`docker cp`
- 【必学】进入容器：`docker exec -it`
- 【建议学】容器网络：bridge、host、none



#### 学习重点

1）理解镜像的分层结构非常重要，这是 Docker 高效的关键。每一层镜像都是只读的，容器在运行时会在顶层创建一个可写层。

2）镜像标签（Tag）用于版本管理，建议使用语义化版本号（如 `1.0.0`、`latest`）。`latest` 标签表示最新版本，但不建议在生产环境使用，因为它可能会变化。

3）端口映射是 Docker 最常用的功能之一，格式为 `-p 宿主机端口:容器端口`。比如 `-p 8080:80` 表示将容器的 80 端口映射到宿主机的 8080 端口。

4）`docker exec -it` 可以进入运行中的容器，非常适合调试。`-it` 参数表示交互式终端，可以像 SSH 一样操作容器。

5）尝试用 `docker logs` 查看容器日志，这是排查问题的重要手段。可以使用 `-f` 参数实时查看日志（类似 `tail -f`）。



#### 学习资源

- [Docker 容器生命周期管理](https://docs.docker.com/engine/reference/commandline/container/)：官方文档
- [Docker Hub](https://hub.docker.com/)：官方镜像仓库



#### 面试题库

- [Docker 镜像与容器面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067352871829505)



### 阶段 3：Docker 网络和数据卷

学习 Docker 的网络和数据持久化。



#### 知识点

**Docker 网络：**

- 【必学】Docker 网络模式：bridge、host、none、container
- 【必学】自定义网络：`docker network create`
- 【必学】容器互联：同一网络内容器通信
- 【建议学】网络驱动：bridge、overlay、macvlan
- 【建议学】端口映射和防火墙

**Docker 数据卷：**

- 【必学】什么是数据卷（Volume）？为什么需要数据卷？
- 【必学】数据卷管理：`docker volume create`、`docker volume ls`、`docker volume rm`
- 【必学】挂载数据卷：`-v`、`--mount`
- 【必学】绑定挂载（Bind Mount）：挂载宿主机目录
- 【建议学】数据卷容器（Data Volume Container）

**数据持久化实战：**

- 【必学】MySQL 数据持久化
- 【必学】Nginx 配置文件持久化
- 【建议学】数据备份和恢复



#### 学习重点

1）Docker 网络是容器之间通信的基础。默认的 bridge 网络可以满足大部分需求，但在复杂场景下，建议使用自定义网络，可以通过容器名称进行通信，更加方便。

2）数据卷是 Docker 数据持久化的标准方式。容器删除后，数据卷中的数据不会丢失。建议在运行数据库、文件存储等有状态服务时，一定要使用数据卷。

3）绑定挂载（Bind Mount）可以将宿主机的目录或文件挂载到容器中，适合开发环境。但在生产环境建议使用数据卷，更加安全和易于管理。

4）`-v` 和 `--mount` 都可以挂载数据卷，`--mount` 的语法更清晰，推荐使用。格式为 `--mount type=volume,source=my-volume,target=/data`。

5）多动手练习：尝试搭建一个 WordPress 个人博客应用（Nginx + PHP + MySQL），通过数据卷持久化数据，通过自定义网络实现容器互联。

6）推荐通过鱼皮的原创项目学习 Docker 实战应用。鱼皮的很多项目（如 OJ 判题系统、AI 超级智能体、亿级流量点赞系统等）都提供了完整的 Docker 部署方案，包括 Dockerfile、docker-compose.yml 配置等，可以直接学习和参考。



#### 学习资源

- ⭐ [Docker 网络详解](https://docs.docker.com/network/)：官方文档
- ⭐ [Docker 数据卷详解](https://docs.docker.com/storage/volumes/)：官方文档



#### 面试题库

- [Docker 网络与存储面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067352871829505)



### 阶段 4：Dockerfile 最佳实践

学习如何编写高质量的 Dockerfile，构建自定义镜像。

![](https://pic.yupi.icu/1/H8mhf23JNy-zCPrLaNs_H4h6K1xLRHv-P0JS4_Ad86xSo7En4tLT3POuOJPrcBNXG5lWDy2Y6fdNzRrzoB9SSLxrHhwrdk-qO28__D19NzO01OkkyBdr7YzZo2K_46HidAoUpmxeW2FOF42uOtAg3Pnfe_gcWafYs7xYywgdFeRdK3kV-p7LfIY7Z9h9tg.png)



#### 知识点

**Dockerfile 基础：**

- 【必学】什么是 Dockerfile？
- 【必学】基础指令：FROM、RUN、COPY、ADD、WORKDIR、EXPOSE、CMD、ENTRYPOINT
- 【必学】构建镜像：`docker build`
- 【必学】镜像标签：`-t` 参数

**Dockerfile 进阶：**

- 【必学】ENV：设置环境变量
- 【必学】ARG：构建参数
- 【必学】VOLUME：声明数据卷
- 【建议学】HEALTHCHECK：健康检查
- 【建议学】ONBUILD：触发器指令
- 【建议学】多阶段构建（Multi-stage Build）

**Dockerfile 最佳实践：**

- 【必学】减少镜像层数：合并 RUN 指令
- 【必学】利用构建缓存：合理排列指令顺序
- 【必学】使用 .dockerignore：忽略不需要的文件
- 【必学】选择合适的基础镜像：alpine vs debian
- 【建议学】使用非 root 用户运行容器
- 【建议学】清理临时文件：减小镜像体积

**镜像优化：**

- 【必学】镜像体积优化
- 【建议学】镜像安全扫描：docker scan、Trivy
- 【建议学】镜像分层优化



#### 学习重点

1）Dockerfile 是构建自定义镜像的脚本，每一行指令都会创建一层镜像。理解指令的作用和执行顺序非常重要。

比如下面就是一个 Dockerfile：

![](https://pic.yupi.icu/1/dotnet-docker-cmds-complete.png)

2）CMD 和 ENTRYPOINT 的区别是面试常考点。简单来说：CMD 定义容器启动时的默认命令，可以被 `docker run` 的参数覆盖；ENTRYPOINT 定义容器启动时的固定命令，不会被覆盖。

3）多阶段构建可以大大减小镜像体积，尤其适合编译型语言（如 Go、Java）。第一阶段用于编译，第二阶段只包含运行时所需的文件，可以减少 90% 以上的体积。

4）Alpine 镜像非常小（只有 5MB 左右），适合作为基础镜像。但它使用的是 musl libc 而不是 glibc，可能存在兼容性问题。如果追求体积，选择 Alpine；如果追求稳定性，选择 Debian。

5）镜像安全很重要，如果是在企业中，可能会使用 Trivy、Clair 等工具扫描镜像漏洞，及时更新基础镜像。

6）可以让 AI 根据你的应用类型（如 Node.js、Java、Go）生成 Dockerfile 模板，再根据需求调整。或者鱼皮推荐直接到 GitHub 上找和你技术栈类似的项目，模仿他们的 Dockerfile 写法就好，像 [鱼皮的很多开源项目](https://github.com/liyupi) 都提供了现成的前端和后端 Dockerfile 给大家拿来用。

7）强烈推荐通过鱼皮的 [OJ 判题系统](https://www.codefather.cn/course/1790980707917017089) 项目学习 Dockerfile 编写和 Docker 代码沙箱实战，这个项目从 0 到 1 带你实战 Docker 容器化部署、编写 Dockerfile、使用 Docker 实现安全的代码沙箱，比单纯学理论效果好得多。



#### 学习资源

- ⭐ [OJ 判题系统](https://www.codefather.cn/course/1790980707917017089)：鱼皮原创，实战 Docker 代码沙箱 + Dockerfile 编写 + 容器化部署
- ⭐ [Dockerfile 官方文档](https://docs.docker.com/engine/reference/builder/)：最权威的学习资料
- ⭐ [Dockerfile 最佳实践](https://docs.docker.com/develop/develop-images/dockerfile_best-practices/)：官方最佳实践
- [黑马程序员 Docker 快速入门到项目部署](https://www.bilibili.com/video/BV1HP4118797)
- [《Docker — 从入门到实践》](https://yeasy.gitbook.io/docker_practice/)：开源书籍，Dockerfile 章节



#### 面试题库

- [Dockerfile 面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067352871829505)



### 阶段 5：Docker Compose

Docker Compose 是管理多容器应用的标准工具，可以通过一个 YAML 文件定义多个容器，一键启动和停止。对于复杂的应用（如 Web 应用 + 数据库 + 缓存），Compose 是必备工具。

本阶段要学习使用 Docker Compose 管理多容器应用。

![](https://pic.yupi.icu/1/1*ltc8W2kSyH7I-KidlugYNQ.png)



#### 知识点

**Docker Compose 基础：**

- 【必学】什么是 Docker Compose？为什么需要 Compose？
- 【必学】安装 Docker Compose
- 【必学】docker-compose.yml 文件结构
- 【必学】基础命令：`docker-compose up`、`docker-compose down`、`docker-compose ps`

**docker-compose.yml 配置：**

- 【必学】services：定义服务
- 【必学】image：指定镜像
- 【必学】build：构建镜像
- 【必学】ports：端口映射
- 【必学】volumes：数据卷挂载
- 【必学】environment：环境变量
- 【必学】depends_on：服务依赖
- 【建议学】networks：自定义网络
- 【建议学】restart：重启策略

**Docker Compose 实战：**

- 【必学】搭建 LNMP 环境（Nginx + MySQL + PHP）
- 【必学】搭建 WordPress 应用
- 【建议学】搭建微服务应用

**Docker Compose 进阶：**

- 【建议学】环境变量文件：.env
- 【建议学】扩展配置：extends、override
- 【建议学】服务扩缩容：`docker-compose scale`



#### 学习建议

1）docker-compose.yml 文件的语法比较简单，重点是理解各个配置项的作用。建议从官方示例开始，逐步学习。

2）`depends_on` 只能保证容器的启动顺序，不能保证服务的就绪状态。比如 MySQL 容器启动后，数据库可能还没有初始化完成。可以使用 `wait-for-it.sh` 等脚本来等待服务就绪。

3）Compose 非常适合本地开发环境，可以快速搭建完整的开发环境。但在生产环境，建议使用 Kubernetes 等容器编排平台。

4）多动手练习：尝试用 Compose 搭建不同的应用，比如 WordPress、GitLab、Nextcloud 等，熟悉 Compose 的使用。

5）鱼皮的一些原创项目（比如 OJ 判题系统）提供了 docker-compose.yml 配置文件，可以一键启动项目所需的所有服务（数据库、缓存、消息队列等）。建议学习这些项目时，重点看一下 docker-compose.yml 的配置，理解如何用 Compose 管理多容器应用。



#### 学习资源

- ⭐ [Docker Compose 官方文档](https://docs.docker.com/compose/)：最权威的学习资料
- [Awesome Compose](https://github.com/docker/awesome-compose)：官方 Compose 示例项目合集
- [鱼皮的原创项目](https://www.codefather.cn/post/1797431216467001345)：多个项目提供 docker-compose.yml 配置参考



#### 面试题库

- [Docker Compose 面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067352871829505)



#### 项目推荐

**鱼皮原创项目（实战 Docker）：**

- ⭐ [OJ 判题系统](https://www.codefather.cn/course/1790980707917017089)：从 0 到 1 实战 Docker 代码沙箱、Dockerfile 编写、容器化部署、单体项目微服务改造，深入学习 Docker 在实际项目中的应用
- [AI 零代码应用生成平台（25年最新）](https://www.codefather.cn/course/1948291549923344386)：微服务全栈项目，学习 Docker 容器化部署、Selenium 自动化、企业级监控体系
- [AI 超级智能体项目（25年最新）](https://www.codefather.cn/course/1915010091721236482)：学习微信云托管 Serverless 部署，体验自动化容器化部署的爽感
- [亿级流量点赞系统（25年最新）](https://www.codefather.cn/course/1912696290659577857)：学习 Docker + Nginx + TiDB + Redis + Pulsar 分布式系统容器化部署和优化
- [AI 自动回复工具（25年最新）](https://www.codefather.cn/course/1966420016440999938)：学习 Docker 容器化部署、企业级架构设计



### 阶段 6：Kubernetes 入门（可跳过）

Kubernetes（K8s）是 Docker 之上的容器编排平台，功能非常强大，但学习难度较大。建议先掌握 Docker，再学习 Kubernetes。

![](https://pic.yupi.icu/1/kubernetes-0.png)

本阶段的目标是学习 Kubernetes 容器编排平台的基础知识。



#### 知识点

**Kubernetes 基础：**

- 【建议学】什么是 Kubernetes？K8s 的架构
- 【建议学】K8s 的核心概念：Pod、Service、Deployment、Namespace
- 【建议学】K8s 集群搭建：Minikube、Kind、云服务（EKS、AKS、GKE）
- 【建议学】kubectl 命令：K8s 的命令行工具

**K8s 核心组件：**

- 【建议学】Pod：K8s 的最小调度单元
- 【建议学】Deployment：管理 Pod 的副本数
- 【建议学】Service：服务发现和负载均衡
- 【建议学】ConfigMap 和 Secret：配置管理
- 【建议学】Ingress：HTTP/HTTPS 路由
- 【可不学】StatefulSet、DaemonSet、Job、CronJob

**K8s 实战：**

- 【建议学】部署第一个应用到 K8s
- 【建议学】使用 Deployment 管理应用版本
- 【建议学】使用 Service 暴露应用



#### 学习建议

1）Kubernetes 的学习资料非常多，但很多都比较复杂。建议从官方教程 [Kubernetes Basics](https://kubernetes.io/docs/tutorials/kubernetes-basics/) 开始，循序渐进。

2）Minikube 是本地运行 K8s 集群的工具，适合学习和开发。Kind（Kubernetes in Docker）也是不错的选择，可以在 Docker 中运行 K8s 集群。

3）kubectl 是 K8s 的命令行工具，类似于 Docker 的 docker 命令。建议重点学习常用命令，如 `kubectl get`、`kubectl describe`、`kubectl apply`、`kubectl logs` 等。

4）K8s 的学习不是一蹴而就的，建议先掌握核心概念（Pod、Service、Deployment），再逐步深入。**如果时间有限，可以先跳过 K8s，等工作中需要时再学习。**



#### 学习资源

- ⭐ [Kubernetes 官方文档](https://kubernetes.io/zh-cn/docs/home/)：最权威的学习资料
- [Kubernetes 中文社区](https://www.kubernetes.org.cn/)：中文学习资源
- [Kubernetes 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754993086443521)：完整的 K8s 学习路线



### 阶段 7：CI/CD 实战

将 Docker 应用到实际的 CI/CD 流程中。



#### 知识点

**CI/CD 基础：**

- 【建议学】什么是 CI/CD？
- 【建议学】常用 CI/CD 工具：Jenkins、GitLab CI、GitHub Actions
- 【建议学】Docker 在 CI/CD 中的作用

**CI/CD 实战：**

- 【建议学】使用 GitHub Actions 自动构建 Docker 镜像
- 【建议学】使用 GitLab CI 部署 Docker 应用
- 【建议学】使用 Jenkins + Docker 实现持续集成

**自动化部署：**

- 【建议学】Docker 镜像自动推送到 Docker Hub
- 【建议学】使用 Webhook 实现自动部署
- 【建议学】蓝绿部署、金丝雀发布



#### 学习重点

1）CI/CD 是 DevOps 的核心，Docker 在 CI/CD 中扮演着重要角色。通过 Docker，可以保证开发、测试、生产环境的一致性。

2）GitHub Actions 是最简单的 CI/CD 工具，非常适合个人项目和小型项目，建议先从 GitHub Actions 开始学习。

![](https://pic.yupi.icu/1/actions-quickstart-logs-20251202102016225.png)

GitLab CI 和 Jenkins 功能更强大，适合企业级项目。如果公司使用这些工具，建议深入学习。

3）自动化部署可以大大提高开发效率，减少人为错误。建议在自己的项目中实践 CI/CD，体验自动化的爽感。



#### 学习资源

- [GitHub Actions 官方文档](https://docs.github.com/cn/actions)：官方学习资料
- [尚硅谷 Docker 教程](https://www.bilibili.com/video/BV1gr4y1U7CY)：讲解详细，适合零基础
- [GitLab CI 官方文档](https://docs.gitlab.com/ee/ci/)：官方学习资料
- [CI/CD 持续集成学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755091384152066)：完整的 CI/CD 学习路线
- [云原生开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755030541578242)：完整的云原生学习路线



### 阶段 8：求职备战

准备 Docker 相关岗位的面试。



#### 学习建议

1）梳理 Docker 知识体系：回顾 Docker 的核心知识点，包括镜像、容器、网络、数据卷、Dockerfile、Compose 等，整理成自己的知识体系。

2）准备项目经历：梳理自己使用 Docker 的项目经历，重点介绍如何使用 Docker 部署应用、如何优化 Dockerfile、如何解决 Docker 相关问题等。

3）多刷面试题：使用 [面试鸭](https://www.mianshiya.com/) 刷题，Docker 的面试题主要考察基础概念、Dockerfile 编写、网络和存储、容器编排等。

![面试鸭刷题神器热门八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8%E7%83%AD%E9%97%A8%E5%85%AB%E8%82%A1%E6%96%87.png)

4）投简历前先了解目标公司使用的技术栈，如果公司使用 Docker + Kubernetes，就重点准备 K8s 相关知识。

5）可以使用 [1 对 1 AI 模拟面试](https://ai.mianshiya.com/) 进行练习，提前适应面试节奏。



#### 面试考察重点

1. Docker 基础：镜像、容器、仓库的概念和区别
2. Dockerfile：常用指令、最佳实践、多阶段构建
3. 网络：网络模式、容器互联、端口映射
4. 数据卷：数据持久化、数据卷管理
5. Docker Compose：docker-compose.yml 配置、多容器管理
6. 容器编排：Kubernetes 基础概念（如果岗位要求）
7. CI/CD：Docker 在 CI/CD 中的应用
8. 实战经验：Docker 在项目中的应用、遇到的问题和解决方案



#### 经典面试题

1. Docker 和虚拟机有什么区别？
2. Docker 镜像的分层结构是什么？为什么要分层？
3. Dockerfile 中 CMD 和 ENTRYPOINT 有什么区别？
4. Docker 容器如何持久化数据？数据卷和绑定挂载有什么区别?
5. Docker 的网络模式有哪些？分别适用于什么场景？
6. 如何优化 Dockerfile，减小镜像体积？
7. 什么是多阶段构建？有什么好处？
8. Docker Compose 中的 depends_on 能保证服务启动顺序吗？



#### 面试题库

- ⭐ [Docker 面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067352871829505)
- [Kubernetes 面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067408974839809)
- [Linux 系统面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067560819048449)



#### Docker 实战项目

- ⭐ [awesome-docker](https://github.com/veggiemonk/awesome-docker)：Docker 资源大全，包含大量实战项目和工具（30k+ stars）
- ⭐ [docker/awesome-compose](https://github.com/docker/awesome-compose)：Docker Compose 官方示例项目集合，包含各种技术栈的容器化实战（35k+ stars）



#### 求职资源

- ⭐ [鱼皮的保姆级求职指南](https://www.codefather.cn/course/job)：从简历到面试的完整指南
- ⭐ [鱼皮的保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)：如何写出高质量简历
- [编程导航的求职干货分享](https://www.codefather.cn/learn?category=76&type=2)：求职经验和技巧
- [老鱼简历](https://www.laoyujianli.com/)：写简历工具 + 简历模板大全
- [真实面经大全](https://www.codefather.cn/job/experience)：了解真实的面试流程
- [几百场真实面试视频](https://www.codefather.cn/mockInterview)：观看他人的面试过程
- [1 对 1 模拟面试](https://ai.mianshiya.com/)：AI 模拟面试练习
- [面试题讲解视频](https://space.bilibili.com/3546383483144655)：面试鸭官方题解



## 写在最后

Docker 是现代软件开发和部署的基础设施，几乎所有的互联网公司都在使用 Docker。掌握 Docker 技能，可以大大提高你的开发和运维效率，也能让你在求职时更有竞争力。

学习 Docker 需要多动手实践，从运行第一个容器开始，逐步学习镜像构建、网络配置、数据持久化、Compose 多容器管理，最后到 Kubernetes 容器编排和 CI/CD 实战。

建议大家按照本路线循序渐进地学习，每个阶段都动手实践，遇到问题多查文档、多搜索、多使用 AI 工具辅助学习。

最后，祝大家学习顺利，早日掌握 Docker，成为更优秀的程序员，加油！💪



## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
