# 2026年最新Kubernetes学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Kubernetes 求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1812067408974839809)



## 开篇介绍

Kubernetes（简称 K8s）是 Google 开源的容器编排平台，后来成为云原生计算基金会的核心项目。

Kubernetes 的名字来源于希腊语，意为舵手或飞行员，象征着它在容器化应用中的指挥和调度作用。

**为什么要学 Kubernetes？**

首先，Kubernetes 是容器编排领域的事实标准，可以自动化部署、扩展和管理容器化应用。如果 Docker 是用来创建和运行单个容器的工具，那么 Kubernetes 就是用来管理成千上万个容器的平台。

![Kubernetes and Docker: What's Best for Your Container Strategy?](https://pic.yupi.icu/1/1730810641838251983_0_Kubernetes%2520&%2520Docker_blog%2520image_1.png)

随着云原生技术的普及，越来越多的公司开始使用 Kubernetes 部署和管理应用。Kubernetes 已经成为云计算时代的基础设施，掌握 Kubernetes 是云原生工程师、DevOps 工程师、后端工程师的必备技能。

目前 Kubernetes 的薪资普遍较高，一线城市的 Kubernetes 工程师平均薪资在 25-50K，比很多开发岗还高！

![](https://pic.yupi.icu/1/image-20251203114643546.png)

在 AI 时代，Kubernetes 也是部署 AI 应用的重要平台，很多 AI 公司使用 Kubernetes 部署和管理 AI 模型服务。



### 学习前提

学习 Kubernetes 建议先掌握：
1. Docker 基础：容器的创建、镜像、Dockerfile 等【必须】。关于 Docker 容器化的详细学习，可以查看 [Docker 容器化学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754953097949186)
2. Linux 基础：Linux 命令、Shell 脚本等【建议】。关于 Linux 的详细学习，可以查看 [Linux 服务器学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755996393320449)
3. 网络基础：TCP/IP、HTTP 等【建议】
4. YAML 语法：Kubernetes 的配置文件使用 YAML【必须】



**Docker 是什么？**

Docker 是一种容器化技术，可以将应用和其依赖打包成一个独立的容器，实现 “一次构建，到处运行”。Docker 容器轻量级、启动快、隔离性好，是现代应用部署的标准方式。

如果还不会 Docker，建议先查看 [Docker 容器化学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754953097949186)。



### 学习路线图

![Kubernetes 学习路线思维导图](https://pic.yupi.icu/roadmap/kubernetes-roadmap.png)



### 就业方向

学完 Kubernetes 后，可以帮助你更好地求职下列岗位：

1. DevOps 工程师：负责应用的持续集成和持续部署
2. 云原生工程师：负责云原生应用的开发和运维
3. 运维工程师：负责 Kubernetes 集群的部署和管理
4. 后端开发工程师：使用 Kubernetes 部署和管理后端服务
5. 架构师：设计基于 Kubernetes 的云原生架构



## 整体学习建议

1）Kubernetes 是容器编排平台，要先掌握 Docker 的基础知识。如果不会 Docker，建议先学习 [Docker 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754953097949186)。

2）Kubernetes 的学习必须结合实践，要在本地或云上搭建 Kubernetes 集群，部署应用，观察 Kubernetes 的工作过程。

本地学习建议：
- 电脑配置较好（8GB+ 内存）：使用 Minikube，功能最完整
- 电脑配置一般（4-8GB 内存）：使用 k3s，资源占用低
- 需要快速测试：使用 Kind，基于 Docker，启动快

云上学习建议：云平台的 K8s 开箱即用，省去搭建麻烦，懒人必备，但是要注意费用。可以申请阿里云、腾讯云的免费试用额度，体验企业级 K8s 服务。

3）Kubernetes 的官方文档非常详细，中文文档质量也很高。遇到问题时，优先查阅官方文档。也可以用 AI 帮你理解概念、生成配置文件。没事儿可以多到鱼皮的 [AI 资源导航](https://ai.codefather.cn/) 中找找 AI 工具和资源。

![AI资源大全网站](https://pic.yupi.icu/1/AI%E8%B5%84%E6%BA%90%E5%A4%A7%E5%85%A8%E7%BD%91%E7%AB%99.png)



## 阶段 1：Kubernetes 基础（5-10 天，仅供参考）

### 学习目标

理解 Kubernetes 的基本概念和架构，掌握 Kubernetes 的安装和基本使用。



### 知识点

**Kubernetes 基础概念【必学】：**

- Kubernetes 的特点和优势
- 容器编排的概念
- Kubernetes vs Docker Swarm
- 云原生的概念

**Kubernetes 架构【必学】：**

- Master 节点（控制平面）
  - API Server：集群的入口
  - Controller Manager：控制器管理器
  - Scheduler：调度器
  - etcd：分布式键值存储
- Worker 节点（工作节点）
  - Kubelet：节点代理
  - Kube-proxy：网络代理
  - Container Runtime：容器运行时（Docker、containerd）

**本地开发环境集群搭建【必学】：**

- Minikube：本地单节点集群，最常用
- Kind：Docker 中的 Kubernetes，轻量快速
- k3s：轻量级 Kubernetes，资源占用低【推荐】
- MicroK8s：Canonical 出品，适合边缘设备

**生产级部署【必学】：**

- Kubeadm：官方推荐的集群部署工具【建议学】
- Rancher：企业级 Kubernetes 管理平台

**云 K8S 平台【建议学】：**

- 阿里云 ACK（容器服务 Kubernetes 版）
- 腾讯云 TKE（容器服务）
- 华为云 CCE（云容器引擎）
- 百度智能云 CCE（容器引擎）

**kubectl 命令【必学】：**

- kubectl 的安装和配置
- 常用命令（get、describe、create、delete、apply）
- 查看日志（logs）
- 进入容器（exec）



### 学习建议

1）Kubernetes 的架构比较复杂，要理解 Master 节点和 Worker 节点的作用。Master 节点负责管理和调度，Worker 节点负责运行容器。

![](https://pic.yupi.icu/1/1*kIcPL1ngFGOLjkWQXRSX4Q.png)

2）本地学习环境推荐：

   - Minikube：功能最完整，适合学习完整的 K8s 特性
   - Kind：基于 Docker，启动快，适合快速测试
   - k3s：资源占用低（只需 512MB 内存），适合低配电脑，而且 k3s 在生产环境也有广泛应用（边缘计算、IoT）

3）如果想要体验云上的 Kubernetes，可以使用国内云平台的免费试用额度，如阿里云 ACK、腾讯云 TKE 等。云平台的 K8s 服务开箱即用，省去了集群搭建的麻烦，但要注意控制费用。

4）kubectl 是 Kubernetes 的命令行工具，要熟练掌握常用命令。这个阶段要多动手，尝试在 Kubernetes 中部署一个简单的应用（如 Nginx），熟悉基本操作。



### 学习资源

**官方文档和教程：**
- ⭐ [Kubernetes 官方文档](https://kubernetes.io/zh-cn/docs/home/)：官方中文文档
- ⭐ [2024 Kubernetes 小白速成课](https://www.bilibili.com/video/BV156421c78m/)：零基础入门
- [Kubernetes（K8S）教程](https://www.bilibili.com/video/BV1MT411x7GH)：全套入门 + 微服务实战项目
- [Kubernetes 入门教程](https://www.bilibili.com/video/BV1Se411r7vY)
- [Kubernetes 入门指南](https://www.hostol.com/archives/2547)：Docker 之后为什么需要容器编排

**快速搭建工具：**
- [k3s 官方文档](https://docs.k3s.io/zh/)：轻量级 Kubernetes
- [Minikube 官方文档](https://minikube.sigs.k8s.io/docs/)：本地开发环境
- [Kind 官方文档](https://kind.sigs.k8s.io/)：Docker 中的 K8s

**国内云平台：**
- [阿里云 ACK 文档](https://help.aliyun.com/zh/ack/)：阿里云容器服务
- [腾讯云 TKE 文档](https://cloud.tencent.com/document/product/457)：腾讯云容器服务
- [华为云 CCE 文档](https://support.huaweicloud.com/cce/index.html)：华为云容器引擎



### 在线练习

- [Kubernetes 官方教程](https://kubernetes.io/zh-cn/docs/tutorials/kubernetes-basics/)：交互式教程



## 阶段 2：核心对象（7-15 天，仅供参考）


核心对象包括 Pod、Deployment、Service 等，是 Kubernetes 中最常用的资源类型，掌握它们是使用 K8s 的基础。



### 学习目标

掌握 Kubernetes 的核心对象，能够编写 YAML 配置文件部署应用。



### 知识点

**Pod【必学】：**

- Pod 的概念（Kubernetes 的最小调度单元）
- Pod 的创建和管理
- Pod 的生命周期
- 多容器 Pod
- Init 容器【建议学】

**Deployment【必学】：**

- Deployment 的概念和作用
- 创建 Deployment
- 滚动更新和回滚
- 副本数管理

**Service【必学】：**

- Service 的概念和作用
- Service 类型（ClusterIP、NodePort、LoadBalancer）
- 服务发现
- Endpoints

**Namespace【必学】：**

- Namespace 的概念和作用
- 创建和管理 Namespace
- 资源隔离

**Label 和 Selector【必学】：**

- Label 的概念和使用
- Selector 的使用



### 学习建议

1）Pod 是 Kubernetes 的核心概念，是最小的调度单元。一个 Pod 可以包含一个或多个容器。

![](https://pic.yupi.icu/1/final1200by676.png)

2）Deployment 是 Kubernetes 管理 Pod 的控制器，可以管理 Pod 的副本数、滚动更新、回滚等。实际应用中很少直接创建 Pod，而是通过 Deployment 来管理 Pod。

3）Service 提供了稳定的网络入口，即使 Pod 重启 IP 变化，Service 的 IP 仍然不变。Service 是 Kubernetes 服务发现和负载均衡的基础。

4）YAML 是 Kubernetes 配置文件的格式，要熟悉 YAML 的语法。建议使用 VS Code + YAML 插件编写 YAML 文件。

5）这个阶段要多写 YAML 配置文件，部署各种应用，熟悉 Kubernetes 的核心对象。



### 学习资源

- [Kubernetes 核心概念](https://kubernetes.io/zh-cn/docs/concepts/)：官方文档



### 面试题库

- [Kubernetes 面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067408974839809)



## 阶段 3：服务发现和负载均衡（3-7 天，仅供参考）


服务发现和负载均衡是 Kubernetes 实现微服务通信的关键机制，通过 Service 和 Ingress 实现。



### 学习目标

掌握 Kubernetes 的服务发现和负载均衡，理解 Ingress 的使用。



### 知识点

**Ingress【必学】：**

- Ingress 的概念和作用
- Ingress Controller（Nginx Ingress Controller）
- Ingress 规则配置
- 域名路由和路径路由

**网络【建议学】：**

- Kubernetes 网络模型
- CNI（Container Network Interface）
- Service Mesh（Istio）【可不学】



### 学习建议

1）Ingress 是 Kubernetes 的七层负载均衡，可以根据域名和路径将流量路由到不同的 Service，Service 是四层负载均衡。

2）Nginx Ingress Controller 是最常用的 Ingress Controller，要学习如何安装和配置。

3）这部分内容相对高级，建议先掌握前面的基础知识，再学习 Ingress。



### 学习资源

- [Ingress 官方文档](https://kubernetes.io/zh-cn/docs/concepts/services-networking/ingress/)：官方文档
- [Ingress 实战教程](https://www.infoq.cn/article/7uxaoepgi1ufz5t197z0)：完整教程



## 阶段 4：存储和配置（3-7 天，仅供参考）


存储和配置管理是 Kubernetes 中的重要环节，包括 Volume、ConfigMap、Secret 等资源的使用。



### 学习目标

掌握 Kubernetes 的存储和配置管理。



### 知识点

**ConfigMap 和 Secret【必学】：**

- ConfigMap：配置管理
- Secret：敏感信息管理
- 环境变量和配置文件

**存储【建议学】：**

- Volume：卷
- PersistentVolume 和 PersistentVolumeClaim
- StorageClass



### 学习建议

1）ConfigMap 用于存储非敏感配置，Secret 用于存储敏感信息（如密码、密钥）。两者的使用方式类似。

2）存储是 Kubernetes 的重要组成部分，但相对复杂。对于初学者，可以先了解基本概念，工作中遇到需求再深入学习。



## 阶段 5：求职备战

### 学习目标

熟练掌握 Kubernetes 常见面试题，准备好简历和项目经历，顺利通过面试。



### 学习建议

1）准备项目经历：简历上要有 Kubernetes 的使用经历，比如部署过什么应用、管理过多大规模的集群等。最好能有实际的 K8s 部署经验，即使是个人项目也可以。

2）准备简历：建议使用 [老鱼简历](https://www.laoyujianli.com/) 快速制作简历；关于如何写好简历，推荐学习鱼皮的 [保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)。

![老鱼简历网站简历模板大全](https://pic.yupi.icu/1/%E8%80%81%E9%B1%BC%E7%AE%80%E5%8E%86%E7%BD%91%E7%AB%99%E7%AE%80%E5%8E%86%E6%A8%A1%E6%9D%BF%E5%A4%A7%E5%85%A8.png)

3）多刷面试题：Kubernetes 的面试题主要包括基础概念、核心对象、网络、存储、高可用等，建议使用 [面试鸭](https://www.mianshiya.com/) 刷题，能大幅提高面试遇到原题的概率。

![面试鸭刷题神器热门八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8%E7%83%AD%E9%97%A8%E5%85%AB%E8%82%A1%E6%96%87.png)

4）面试时要能够说出 Kubernetes 的核心优势：自动化部署、自动扩缩容、自愈能力、负载均衡等，以及它适用的场景。

更多求职干货：[编程导航求职干货分享](https://www.codefather.cn/learn?category=76&type=2)



### 经典面试题

**基础概念：**

1. Kubernetes 是什么？有什么作用？
2. Kubernetes 和 Docker 有什么区别？
3. Kubernetes 的架构是怎样的？
4. Master 节点和 Worker 节点有什么区别？

**核心对象：**

1. Pod 是什么？
2. Deployment 和 Pod 有什么区别？
3. Service 是什么？有哪些类型？
4. Ingress 是什么？和 Service 有什么区别？

**其他：**

1. 如何实现服务的自动扩缩容？
2. Kubernetes 如何实现服务发现？
3. ConfigMap 和 Secret 有什么区别？



### 面试题库

- ⭐ [Kubernetes 面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067408974839809)
- [Docker 面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067352871829505)
- [DevOps 运维面试题 - 面试鸭](https://www.mianshiya.com/bank/1811358596541374466)



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
- ⭐ [Kubernetes 官方文档](https://kubernetes.io/zh-cn/docs/home/)：最权威的学习资料
- [Docker 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990754953097949186)：完整的 Docker 学习路线

### Kubernetes 专题资源

- [Kubernetes GitHub](https://github.com/kubernetes/kubernetes)：Kubernetes 源码

### Kubernetes 实战项目

- ⭐ [kubernetes/examples](https://github.com/kubernetes/examples)：Kubernetes 官方示例教程集合（6.5k+ stars）
- [k8s-tutorials](https://github.com/guangzhengli/k8s-tutorials)：K8s 中文教程，从入门到实战（5k+ stars）
- [awesome-kubernetes](https://github.com/ramitsurana/awesome-kubernetes)：Kubernetes 资源大全
- [CNCF](https://www.cncf.io/)：云原生计算基金会

### 技术博客

- [Kubernetes 官方博客](https://kubernetes.io/blog/)：K8s 官方技术博客
- [CNCF 博客](https://www.cncf.io/blog/)：云原生计算基金会博客
- [Google Cloud Blog](https://cloud.google.com/blog)：谷歌云 Kubernetes 实践
- [Airbnb Tech Blog](https://medium.com/airbnb-engineering)：Airbnb K8s 架构



## 总结一下

Kubernetes 是云原生时代的核心技术，掌握 Kubernetes 能够大大提升你的技术竞争力。虽然 Kubernetes 的学习曲线较陡峭，概念较多，但只要理解了 Kubernetes 的设计理念和核心概念，后续的学习就会轻松很多。

学习 Kubernetes 要先掌握 Docker 的基础知识，然后理解 Kubernetes 的架构和核心对象。建议在本地搭建 Kubernetes 集群进行实践，部署各种应用，观察 Kubernetes 的工作过程。

希望这份学习路线能够帮助大家少走弯路，更高效地掌握 Kubernetes。加油小伙伴们 💪🏻！

![](https://pic.yupi.icu/1/%E5%8A%A0%E6%B2%B9%E8%A1%A8%E6%83%85%E5%8C%85.jpeg)




## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
