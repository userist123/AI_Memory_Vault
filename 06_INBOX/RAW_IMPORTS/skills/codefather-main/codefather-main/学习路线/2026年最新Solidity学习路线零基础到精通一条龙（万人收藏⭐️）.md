# 2026年最新Solidity学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Solidity 求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1991434686466883585)



## 开篇介绍

Solidity 是一门面向智能合约的编程语言，专为以太坊虚拟机（EVM）设计。



**为什么要学 Solidity？**

首先，Solidity 是智能合约开发的必备语言，也是目前最流行的智能合约语言。几乎所有以太坊上的 DApp、DeFi 协议、NFT 项目都使用 Solidity 编写智能合约。可以说 Solidity 是进入区块链开发领域的敲门砖。掌握 Solidity，不仅能让你开发智能合约、DApp，还能有机会从事智能合约审计、甚至是安全研究等高薪工作。

其次，Solidity 相对容易学习，语法类似于 JavaScript 和 C++，有编程基础的话，几周就能上手。而且 Solidity 的应用场景非常广泛：从去中心化金融（DeFi）到非同质化代币（NFT），从去中心化自治组织（DAO）到游戏（GameFi）。从 Uniswap 到 Aave，从 CryptoPunks 到 Bored Ape Yacht Club，这些知名项目的智能合约都是用 Solidity 编写的。

![](https://pic.yupi.icu/1/image-20251202215437564.png)

**要注意，Solidity 智能合约的安全至关重要！**

智能合约一旦部署无法修改，漏洞会永久存在，可能导致重大资金损失。学习 Solidity 时，要特别注重安全最佳实践，了解常见漏洞（如重入攻击、整数溢出、权限控制不当等）和防御方法。



### 学习前提

学习 Solidity 建议先掌握：
1. 编程基础：至少会一门编程语言（JavaScript、Python、C++ 等）【必学】。关于 JavaScript 的详细学习，可以查看 [JavaScript 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990753900055015426)
2. 区块链基础：理解区块链和以太坊的基本概念【建议】。关于区块链开发的详细学习，可以查看 [区块链开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990756125376557057)



### 学习路线图

![Solidity 学习路线思维导图](https://pic.yupi.icu/roadmap/solidity-roadmap.png)

### 就业方向

学完 Solidity 后，可以从事以下岗位：

1. 智能合约工程师：开发和优化智能合约
2. 区块链开发工程师：开发区块链应用
3. 智能合约审计工程师：审计智能合约安全
4. DeFi 开发工程师：开发去中心化金融协议
5. Web3 开发工程师：开发去中心化应用



## 整体学习建议

1）Solidity 的语法类似 JavaScript，如果会 JavaScript，学习 Solidity 会很快。但 Solidity 有很多区块链特有的概念（如 Gas、address、mapping 等），要仔细学习。

2）智能合约的安全非常重要，要学习安全最佳实践，使用安全库（如 OpenZeppelin），避免常见漏洞。记住：**合约一旦部署无法修改，安全问题会永久存在！**

3）GitHub 上有很多优秀的 Solidity 项目（比如 Uniswap、Aave），建议多多阅读源码来学习最佳实践。看别人怎么写，比自己瞎琢磨效率高得多。

4）Solidity 的学习一定要结合实践。建议在 Remix IDE 中练习，编写各种智能合约，部署到测试网。Remix 是在线 IDE，无需安装，非常方便。



## 阶段 1：Solidity 基础语法（10-20 天，仅供参考）

### 学习目标

掌握 Solidity 的基础语法，能够编写简单的智能合约。



### 知识点

**基础语法【必学】：**

- 数据类型（uint、int、address、bool、string、bytes）
- 变量（状态变量、局部变量、全局变量）
- 函数
- 控制流（if、for、while）
- 运算符

**函数【必学】：**

- 函数的定义
- 可见性（public、private、internal、external）
- 状态可变性（pure、view、payable）
- 修饰符（modifier）
- 返回值

**特殊变量和函数【必学】：**

- msg.sender、msg.value
- block.timestamp、block.number
- address.balance、address.transfer、address.call



### 学习重点

1）Solidity 的数据类型和其他语言类似，但有一些特殊类型。address 是以太坊地址类型，uint 是无符号整数（常用 uint256）。

2）函数的可见性非常重要，直接影响安全。public 可以被任何人调用，private 只能在合约内部调用，external 只能从外部调用，internal 可以在合约内部和子合约中调用。

3）状态可变性也很重要。view 函数不修改状态，pure 函数不读取也不修改状态，payable 函数可以接收以太币。

4）建议在 [Remix IDE](https://remix.ethereum.org/) 中练习基础语法，Remix 是在线 IDE，无需安装，非常方便。

![](https://pic.yupi.icu/1/image-20251202111223393-20251202215707491.png)



### 学习资源

- ⭐ [Solidity 官方文档](https://docs.soliditylang.org/)：最权威的学习资料
- [WTF Solidity](https://vampireachao.gitee.io/2025/09/08/WTF-Solidity/)：Solidity 教程
- [Solidity 快速入门教程](https://www.bilibili.com/video/BV1S5pqeBEfp)
- [Solidity 智能合约开发入门实战](https://www.bilibili.com/video/BV15fBJYUEZq)



## 阶段 2：面向对象和高级特性（12-25 天，仅供参考）

### 学习目标

掌握 Solidity 的面向对象特性和高级功能，这些是编写复杂智能合约的基础。



### 知识点

**结构化数据【必学】：**

- mapping（映射）
- array（数组）
- struct（结构体）
- enum（枚举）

**面向对象【必学】：**

- 合约（contract）
- 继承
- 抽象合约和接口
- 多重继承

**事件和错误【必学】：**

- event（事件）
- error（自定义错误，Solidity 0.8+）
- require、assert、revert

**库【建议学】：**

- library（库）
- using for



### 学习建议

1）mapping 是 Solidity 中最常用的数据结构，类似于哈希表。mapping 不能遍历，也不能获取长度，要注意这些限制。

2）继承是 Solidity 的重要特性，可以复用代码。Solidity 支持多重继承，要理解继承的顺序和规则。

3）事件可以让合约向外部发送通知，前端可以监听事件。事件的 Gas 消耗低，常用于记录重要操作。

4）Solidity 0.8+ 引入了自定义错误，相比字符串错误更省 Gas，更易于处理。建议使用自定义错误。



### 学习资源

- [Solidity 官方文档 - 高级特性](https://docs.soliditylang.org/)：官方文档



## 阶段 3：安全最佳实践（12-25 天，仅供参考）

### 学习目标

掌握 Solidity 安全编程，能够编写安全的智能合约，避免资产损失。



### 知识点

**常见漏洞【必学，核心重点】：**

- 重入攻击：最危险的漏洞
- 整数溢出/下溢：Solidity 0.8+ 默认检查
- 权限控制不当：未正确验证调用者
- tx.origin 钓鱼：使用 tx.origin 而不是 msg.sender
- 未检查的外部调用：call 的返回值未检查

**安全模式【必学】：**

- Checks-Effects-Interactions 模式
- Pull Payment 模式
- Emergency Stop（Pausable）

**OpenZeppelin 安全库【必学】：**

- ReentrancyGuard（防重入）
- Ownable（权限控制）
- Pausable（暂停机制）
- SafeERC20



### 学习重点

1）重入攻击防御：

   - 使用 Checks-Effects-Interactions 模式：先检查条件，再更新状态，最后调用外部
   - 使用 OpenZeppelin 的 ReentrancyGuard 修饰符
   - 使用 transfer/send 而不是 call（但 transfer/send 已不推荐）

2）整数溢出防御：Solidity 0.8+ 默认检查溢出，无需 SafeMath。如果使用 0.7 或更早版本，必须使用 SafeMath。

3）权限控制：敏感函数要加上权限检查，使用 OpenZeppelin 的 Ownable 或 AccessControl。

4）安全是 Solidity 开发的重中之重，要深入学习。建议阅读 [智能合约安全学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990756156976443394) 中的安全部分。



### 学习资源

- [智能合约安全之重入攻击](https://learnblockchain.cn/article/8453)：详细讲解
- [如何防止 Solidity 重入攻击](https://blog.usdtbi.com/ruhefangzhisolidityzhongrugongjidefikaifazhebikandeanquanzhinan.html)：DeFi 安全指南



## 阶段 4：项目实战（15-30 天，仅供参考）

### 学习目标

通过实际项目巩固所学知识，积累 Solidity 项目经验。



### 学习建议

1）从标准合约开始：先实现 ERC-20、ERC-721 等标准合约，理解代币标准。

2）开发 DeFi 合约：尝试开发简单的 DeFi 合约（如 AMM、借贷等），理解 DeFi 的工作原理。

3）阅读优秀代码：阅读 Uniswap、Aave、OpenZeppelin 等项目的代码，学习最佳实践。

4）安全审计：为自己的合约进行安全审计，使用工具检测漏洞，手动审查代码。



### 项目推荐

- ERC-20 代币
- ERC-721 NFT
- 多签钱包
- 去中心化交易所（DEX）
- 借贷协议

**优质开源项目：**

- ⭐ [Full Blockchain Solidity Course](https://github.com/smartcontractkit/full-blockchain-solidity-course-js)：32k+ stars，完整的 Solidity 课程和项目
- [Awesome Solidity Projects](https://github.com/0xisk/awesome-solidity-projects)：智能合约优秀项目索引
- [Beginner Solidity Projects](https://github.com/zubairahm3d/Beginner-Level-Solidity-Projects)：100 个初学者项目
- [OpenZeppelin Contracts](https://github.com/OpenZeppelin/openzeppelin-contracts)：25k+ stars，安全的智能合约库



### 学习资源

- [Solidity Foundry 框架教程](https://www.bilibili.com/video/BV1u8411k7Z7/)：零基础教程



## 求职备战

### 学习建议

1）准备智能合约项目：简历上一定要有至少 1 个 Solidity 智能合约项目。面试时要能够讲解合约的设计、安全措施、Gas 优化等。最好能够提供已部署的合约地址和代码仓库。

2）准备简历：可以使用 [老鱼简历](https://www.laoyujianli.com/) 上现成的模板来快速制作简历；关于如何写好简历，推荐学习鱼皮的 [保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)。

![老鱼简历网站简历模板大全](https://pic.yupi.icu/1/%E8%80%81%E9%B1%BC%E7%AE%80%E5%8E%86%E7%BD%91%E7%AB%99%E7%AE%80%E5%8E%86%E6%A8%A1%E6%9D%BF%E5%A4%A7%E5%85%A8.png)

3）多刷面试题：Solidity 的面试题主要包括语法特性、安全漏洞、Gas 优化、标准合约等，可以直接用 [面试鸭](https://www.mianshiya.com/) 网站或者小程序来刷题。

![面试鸭刷题神器热门八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8%E7%83%AD%E9%97%A8%E5%85%AB%E8%82%A1%E6%96%87.png)

4）安全是 Solidity 面试的核心考点，面试时尽量展现你的安全意识，能够说出常见的智能合约漏洞和防范措施，会给面试官留下 “你有经验” 的印象。

更多求职干货：[编程导航求职干货分享](https://www.codefather.cn/learn?category=76&type=2)



### 经典面试题

1. Solidity 有什么特点？
2. storage 和 memory 有什么区别？
3. 如何防止重入攻击？
4. ERC-20 和 ERC-721 有什么区别？
5. 如何优化 Gas 消耗？



### 求职资源

- ⭐ [鱼皮的保姆级求职指南](https://www.codefather.cn/course/job)
- ⭐ [鱼皮的保姆级写简历指南](https://www.codefather.cn/course/1802644557818343425)
- [编程导航的求职干货分享](https://www.codefather.cn/learn?category=76&type=2)
- [老鱼简历](https://www.laoyujianli.com/)
- [真实面经大全](https://www.codefather.cn/job/experience)
- [几百场真实面试视频](https://www.codefather.cn/mockInterview)
- [1 对 1 模拟面试](https://ai.mianshiya.com/)
- [面试题讲解视频](https://space.bilibili.com/3546383483144655)



## 更多资源

### 知识总结

- ⭐ [编程导航](https://www.codefather.cn/)：学习路线、项目教程、面试题、编程资源一站式平台
- [区块链开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990756125376557057)：完整的区块链路线
- [智能合约开发学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990756156976443394)：完整的智能合约路线

### Solidity 资源

- [Solidity 官网](https://soliditylang.org/)：官方网站
- [OpenZeppelin](https://openzeppelin.com/)：安全合约库
- [登链社区](https://learnblockchain.cn/)：中文区块链社区

### 技术博客

- [Solidity 官方博客](https://blog.soliditylang.org/)：Solidity 官方技术博客
- [Ethereum Foundation Blog](https://blog.ethereum.org/)：以太坊官方博客
- [ConsenSys Blog](https://consensys.io/blog)：智能合约开发最佳实践
- [OpenZeppelin Blog](https://blog.openzeppelin.com/)：智能合约安全



## 最后总结

Solidity 是智能合约开发的核心语言，而且 Solidity 的语法相对简单，但是智能合约的安全要求很高，要特别注重安全学习。

学习 Solidity 要先掌握基础语法，理解面向对象特性，深入学习安全最佳实践。一定要多做项目，在测试网络上部署合约，积累开发经验。

再次提醒！智能合约一旦部署无法修改，要充分测试和审计。使用安全库、遵循最佳实践、了解常见漏洞，是每个 Solidity 开发者的必修课。

最后希望这份学习路线能够帮助大家少走弯路，安全、高效地掌握 Solidity，拿到理想的薪资！

![](https://pic.yupi.icu/1/003MWcpMly8guc1elclnoj608c08b0st02.jpg)




## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
