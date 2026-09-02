# 2026年最新Shell脚本学习路线零基础到精通一条龙（万人收藏⭐️）

> 编程导航学习网站：[学编程、做项目、拿 Offer！](https://www.codefather.cn)
> 
> 企业高频面试题库：[开始刷题，面试遇原题！](https://www.mianshiya.com)
>
> 精选简历模板大全：[1 分钟搞定简历！](https://www.laoyujianli.com)
>
> AI 资源导航网站：[获取最新 AI 黑科技！](https://ai.codefather.cn)
>
> 1 对 1 模拟面试：[随时随地提升面试能力](https://ai.mianshiya.com)



Shell 脚本求职高频面试题：[开始刷题](https://www.mianshiya.com/bank/1812067519566053377)



## 开篇介绍

### Shell 脚本是什么？

Shell 是 Linux/Unix 系统的命令行解释器，Shell 脚本就是由 Shell 命令组成的可执行文件。通过编写 Shell 脚本，我们可以将一系列命令组合起来，实现自动化任务，大大提高工作效率。

Shell 脚本广泛应用于系统管理、自动化运维、数据处理、软件部署等场景。比如自动备份数据库、批量处理文件、定时执行任务、监控系统状态、自动化部署应用等，这些工作都可以通过 Shell 脚本来完成。



### 为什么要学 Shell 脚本？

对于运维工程师来说，Shell 脚本是必备技能；对于后端开发工程师来说，Shell 脚本能够帮助你快速完成一些自动化任务，提高开发效率。而且 Shell 脚本的学习成本相对较低，语法简单，几天时间就能掌握基础用法。

在 AI 时代，虽然有了很多自动化工具和平台，但 Shell 脚本仍然是最灵活、最高效的自动化方案。而且现在可以使用 AI 工具（比如 ChatGPT、Gemini、DeepSeek）帮你生成 Shell 脚本，大大降低了学习门槛。



### 学习前提

学习 Shell 脚本需要具备基本的 Linux 命令行知识。如果你还不熟悉 Linux 命令，可以查看 [Linux 服务器学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755996393320449)。



### 学习路线图

![Shell 脚本学习路线思维导图](https://pic.yupi.icu/roadmap/shell-scripting-roadmap.png)



## 整体学习建议

1）边学边练：Shell 脚本的学习必须结合实践，要多写脚本、多调试。建议在 Linux 系统或 macOS（内置 Bash）上练习。

![](https://pic.yupi.icu/1/image-20251126232312025.png)

或者利用 [Shell playground 在线工具](https://codapi.org/shell/) 随时随地练习：

![](https://pic.yupi.icu/1/image-20251126233129574.png)

2）从简单到复杂：不要一开始就写复杂的脚本，先从简单的任务开始（如打印文本、创建文件），逐步增加复杂度。

3）善用 AI 工具：遇到不懂的命令或错误，可以让 AI 工具（如 ChatGPT）帮你解释和调试。可以到鱼皮的 [AI 资源大全](https://ai.codefather.cn/) 中看看有没有适合你的工具。

![AI资源大全网站](https://pic.yupi.icu/1/AI%E8%B5%84%E6%BA%90%E5%A4%A7%E5%85%A8%E7%BD%91%E7%AB%99.png)

4）学习 awk 和 sed：awk 和 sed 是 Shell 脚本中常用的文本处理工具，功能强大，建议重点学习。

5）注意 Shell 的选择：本路线以 Bash 为主，这是 Linux 系统最常用的 Shell。其他 Shell（如 Zsh、Fish）的语法略有不同。



## 阶段 1：Shell 基础（1-5 天）

### 学习目标

理解 Shell 脚本的基本概念，掌握 Shell 脚本的编写和执行方法。



### 知识点

**Shell 基础【必学】：**

- 什么是 Shell
- 常见的 Shell 类型（Bash、Zsh、Ksh、Csh）
- Shebang（#!/bin/bash）
- 脚本的执行方式（bash script.sh、./script.sh、source script.sh）
- 脚本权限（chmod +x）

**注释【必学】：**

- 单行注释（#）
- 多行注释（:<<'COMMENT' ... COMMENT）

**输出【必学】：**

- echo 命令
- printf 命令

**输入【必学】：**

- read 命令
- 命令行参数（$1、$2、$@、$#）



### 学习重点

1）Shebang（`#!/bin/bash`）必须放在脚本的第一行，告诉系统使用哪个解释器来执行脚本。

2）脚本执行方式的区别：`bash script.sh` 会创建新的子进程执行脚本，`source script.sh` 在当前进程执行脚本，变量会保留。

3）建议养成良好的编码习惯，每个脚本都要添加注释，说明脚本的功能、作者、日期等。



### 学习资源

- [尚硅谷 Linux、Shell 教程](https://www.bilibili.com/video/BV1WY4y1H7d3)
- [Shell 编程从入门到精通](https://cloud.tencent.com/developer/article/1763225)：完整教程



### 示例脚本

```bash
#!/bin/bash
# 第一个 Shell 脚本
# 作者：鱼皮
# 日期：2025-11-15

echo "Hello, Shell!"
echo "当前用户：$USER"
echo "当前目录：$(pwd)"
```



## 阶段 2：变量和参数（1-5 天）

### 学习目标

掌握 Shell 变量的定义和使用，理解特殊变量的含义。



### 知识点

**变量定义【必学】：**

- 变量定义（无空格：name="value"）
- 使用变量（$name 或 ${name}）
- 只读变量（readonly）
- 删除变量（unset）

**变量类型【必学】：**

- 局部变量
- 环境变量（export）
- Shell 变量

**特殊变量【必学】：**

- $0：脚本名称
- $1、$2...：位置参数
- $#：参数个数
- $@：所有参数（列表形式）
- $*：所有参数（字符串形式）
- $?：上一条命令的退出状态
- $$：当前进程 ID

**字符串操作【建议学】：**

- 字符串拼接
- 获取字符串长度（${#string}）
- 提取子字符串（${string:start:length}）
- 字符串替换（${string/old/new}）

**数组【建议学】：**

- 定义数组（arr=(1 2 3)）
- 访问数组元素（${arr[0]}）
- 获取数组长度（${#arr[@]}）
- 遍历数组



### 学习重点

1）变量定义时等号两边不能有空格，这是 Shell 的语法规则。`name="value"` 是正确的，`name = "value"` 会报错。

2）使用变量时建议加花括号 `${name}`，这样可以避免歧义，特别是在字符串拼接时。

3）特殊变量非常重要，`$?` 可以用来判断命令是否执行成功（0 表示成功，非 0 表示失败）。



### 示例脚本

```bash
#!/bin/bash
# 变量和参数示例

# 定义变量
name="编程导航"
age=5

# 使用变量
echo "网站名称：${name}"
echo "运营年限：${age}年"

# 命令行参数
echo "脚本名称：$0"
echo "第一个参数：$1"
echo "参数个数：$#"
echo "所有参数：$@"

# 字符串操作
url="https://www.codefather.cn"
echo "URL 长度：${#url}"
echo "域名：${url:8}"

# 数组
sites=("编程导航" "面试鸭" "SQL自学网")
echo "第一个网站：${sites[0]}"
echo "网站数量：${#sites[@]}"
```



## 阶段 3：流程控制（2-7 天）

### 学习目标

掌握 Shell 脚本的流程控制语句，能够编写包含条件判断和循环的脚本。



### 知识点

**条件判断【必学】：**

- test 命令
- `[ ]` 表达式
- `[[ ]]` 表达式（推荐）
- 文件判断（-e、-f、-d、-r、-w、-x）
- 数值比较（-eq、-ne、-gt、-lt、-ge、-le）
- 字符串比较（=、!=、-z、-n）
- 逻辑运算（-a、-o、!）

**if 语句【必学】：**

- if...then...fi
- if...then...else...fi
- if...then...elif...else...fi

**case 语句【必学】：**

- case...in...esac

**循环语句【必学】：**

- for 循环（for i in list; do...done）
- while 循环（while condition; do...done）
- until 循环（until condition; do...done）
- break 和 continue



### 学习重点

1）推荐使用 `[[ ]]` 而不是 `[ ]`，`[[ ]]` 功能更强大，支持正则匹配，而且不需要对变量加引号。

2）文件判断非常常用，如 `-f file` 判断文件是否存在且为普通文件，`-d dir` 判断目录是否存在。

3）for 循环可以遍历命令输出、数组、文件列表等，非常灵活。



### 示例脚本

```bash
#!/bin/bash
# 流程控制示例

# if 语句
age=20
if [[ $age -ge 18 ]]; then
    echo "已成年"
else
    echo "未成年"
fi

# case 语句
read -p "请输入你的选择（1-3）：" choice
case $choice in
    1)
        echo "你选择了选项1"
        ;;
    2)
        echo "你选择了选项2"
        ;;
    3)
        echo "你选择了选项3"
        ;;
    *)
        echo "无效选择"
        ;;
esac

# for 循环
echo "打印1到5："
for i in {1..5}; do
    echo $i
done

# while 循环
echo "倒计时："
count=5
while [[ $count -gt 0 ]]; do
    echo $count
    ((count--))
    sleep 1
done
echo "时间到！"

# 遍历文件
echo "当前目录的文件："
for file in *; do
    if [[ -f $file ]]; then
        echo "文件：$file"
    fi
done
```



## 阶段 4：函数和文件操作（2-7 天）

### 学习目标

掌握 Shell 函数的定义和使用，学习常用的文件操作和文本处理工具。



### 知识点

**函数【必学】：**

- 函数定义（function name() { }）
- 函数调用
- 函数参数（$1、$2...）
- 函数返回值（return）

**文件操作【必学】：**

- 读取文件（cat、head、tail、less、more）
- 写入文件（> 覆盖、>> 追加）
- 重定向（标准输入、标准输出、标准错误）

**文本处理三剑客【必学，重点】：**

- **grep**：搜索文本
  - 基本用法（grep pattern file）
  - 正则表达式匹配
  - 常用选项（-i 忽略大小写、-v 反向匹配、-n 显示行号、-r 递归搜索）
- **sed**：文本替换和编辑
  - 替换（sed 's/old/new/' file）
  - 删除行（sed '/pattern/d' file）
  - 插入行（sed 'i\text' file）
- **awk**：文本分析和处理
  - 基本用法（awk '{print $1}' file）
  - 内置变量（$0 整行、$1第一列、NR行号、NF列数）
  - 条件和循环

**其他常用命令【建议学】：**

- find：查找文件
- xargs：构建命令行
- cut：提取列
- sort：排序
- uniq：去重
- wc：统计行数/单词数/字符数



### 学习建议

1）grep、sed、awk 被称为 “文本处理三剑客”，是 Shell 脚本中最重要的工具，一定要重点学习。

![](https://pic.yupi.icu/1/GGMmdY8bsAAIo0H.jpeg)

2）awk 功能非常强大，可以当作一门编程语言来学习。awk 特别适合处理结构化文本（如日志文件、CSV 文件）。

3）sed 主要用于文本替换和编辑，比如批量替换文件内容、删除特定行等。

4）这些工具一般通过管道（|）组合使用，实现复杂的文本处理任务。



### 学习资源

- [Shell 自动化运维实战](https://comate.baidu.com/zh/page/ak43wjt9jkp)：完整教程
- [运维三剑客实战](https://cloud.tencent.com/developer/article/2381804)：grep、awk、sed



### 示例脚本

```bash
#!/bin/bash
# 函数和文本处理示例

# 函数定义
function greet() {
    echo "Hello, $1!"
}

# 调用函数
greet "鱼皮"

# 读取文件并统计行数
if [[ -f "/etc/passwd" ]]; then
    line_count=$(wc -l < /etc/passwd)
    echo "/etc/passwd 有 $line_count 行"
fi

# grep 示例：搜索包含 "root" 的行
echo "搜索 /etc/passwd 中的 root："
grep "root" /etc/passwd

# awk 示例：提取第一列
echo "提取 /etc/passwd 的第一列："
awk -F: '{print $1}' /etc/passwd | head -5

# sed 示例：替换文本
echo "hello world" | sed 's/world/Shell/'

# 管道组合：统计当前目录的文件数
echo "当前目录的文件数："
ls -l | grep "^-" | wc -l
```



## 阶段 5：实战应用（3-10 天）

### 学习目标

通过实际案例掌握 Shell 脚本的实战技巧，能够编写实用的自动化脚本，比如系统监控、日志分析、自动化部署等场景。



### 实战案例

现在有 AI 了，其实只要能看懂下面的脚本问题就不大~

**案例 1：系统监控脚本**

```bash
#!/bin/bash
# 系统监控脚本

echo "========== 系统信息 =========="
echo "主机名：$(hostname)"
echo "内核版本：$(uname -r)"
echo "运行时间：$(uptime -p)"

echo "========== CPU 使用率 =========="
top -bn1 | grep "Cpu(s)" | awk '{print "CPU使用率：" 100-$8 "%"}'

echo "========== 内存使用情况 =========="
free -h | grep "Mem" | awk '{print "总内存：" $2 "\n已用内存：" $3 "\n可用内存：" $4}'

echo "========== 磁盘使用情况 =========="
df -h | grep "^/dev"
```



**案例 2：自动备份脚本**

```bash
#!/bin/bash
# 数据库备份脚本

# 配置
DB_NAME="mydb"
DB_USER="root"
DB_PASS="password"
BACKUP_DIR="/backup/mysql"
DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="$BACKUP_DIR/${DB_NAME}_${DATE}.sql"

# 创建备份目录
mkdir -p $BACKUP_DIR

# 备份数据库
echo "开始备份数据库 $DB_NAME..."
mysqldump -u$DB_USER -p$DB_PASS $DB_NAME > $BACKUP_FILE

# 检查备份是否成功
if [[ $? -eq 0 ]]; then
    echo "备份成功：$BACKUP_FILE"
    
    # 压缩备份文件
    gzip $BACKUP_FILE
    echo "压缩完成：${BACKUP_FILE}.gz"
    
    # 删除7天前的备份
    find $BACKUP_DIR -name "*.gz" -mtime +7 -delete
    echo "清理完成"
else
    echo "备份失败！"
    exit 1
fi
```



**案例 3：批量处理文件**

```bash
#!/bin/bash
# 批量重命名图片文件

# 检查参数
if [[ $# -lt 1 ]]; then
    echo "用法：$0 <目录>"
    exit 1
fi

DIR=$1
if [[ ! -d $DIR ]]; then
    echo "错误：目录不存在"
    exit 1
fi

# 批量重命名
count=1
for file in $DIR/*.jpg; do
    if [[ -f $file ]]; then
        new_name="$DIR/image_$(printf "%03d" $count).jpg"
        mv "$file" "$new_name"
        echo "重命名：$file -> $new_name"
        ((count++))
    fi
done

echo "处理完成，共重命名 $((count-1)) 个文件"
```



**案例 4：日志分析脚本**

```bash
#!/bin/bash
# 分析 Nginx 访问日志

LOG_FILE="/var/log/nginx/access.log"

if [[ ! -f $LOG_FILE ]]; then
    echo "错误：日志文件不存在"
    exit 1
fi

echo "========== Nginx 日志分析 =========="

# 统计总访问量
total=$(wc -l < $LOG_FILE)
echo "总访问量：$total"

# 统计访问最多的 IP
echo "访问最多的 IP Top 10："
awk '{print $1}' $LOG_FILE | sort | uniq -c | sort -rn | head -10

# 统计访问最多的 URL
echo "访问最多的 URL Top 10："
awk '{print $7}' $LOG_FILE | sort | uniq -c | sort -rn | head -10

# 统计状态码分布
echo "HTTP 状态码分布："
awk '{print $9}' $LOG_FILE | sort | uniq -c | sort -rn
```



### 学习建议

1）实战案例要结合实际需求，不要为了写脚本而写脚本。思考工作中有哪些重复性的任务可以用脚本自动化。

2）脚本要考虑错误处理，如检查文件是否存在、命令是否执行成功、参数是否正确等。

3）复杂的脚本要添加注释，方便自己和他人理解。

4）定时执行脚本可以使用 cron 定时任务，如每天凌晨 2 点执行备份脚本。



## 求职备战

### 学习建议

Shell 脚本一般不是单独的面试科目，而是作为 Linux 运维或后端开发岗位的一部分来考察。面试时可能会要求现场编写简单的 Shell 脚本，或者询问 Shell 脚本的相关知识。

1）准备常用脚本：准备几个常用的 Shell 脚本案例（如系统监控、自动备份、日志分析），能够快速说出脚本的功能和实现思路。

2）掌握文本处理三剑客：grep、sed、awk 是面试的高频考点，要能够熟练使用。

3）理解脚本的执行流程：要理解 Shell 脚本的执行过程，如 Shebang 的作用、变量的作用域、管道的工作原理等。



### 经典面试题

1. Shell 脚本的第一行 #!/bin/bash 有什么作用？
2. source、sh、bash、./ 执行脚本有什么区别？
3. $# 和 $@ 有什么区别？
4. 如何判断一个文件是否存在？
5. 如何在 Shell 脚本中捕获命令的输出？
6. grep、sed、awk 各有什么特点？如何选择？
7. 如何在 Shell 脚本中实现异常处理？



### 面试题库

- [Shell 面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067519566053377)
- [Linux 系统面试题 - 面试鸭](https://www.mianshiya.com/bank/1812067560819048449)

![面试鸭刷题神器热门八股文](https://pic.yupi.icu/1/%E9%9D%A2%E8%AF%95%E9%B8%AD%E5%88%B7%E9%A2%98%E7%A5%9E%E5%99%A8%E7%83%AD%E9%97%A8%E5%85%AB%E8%82%A1%E6%96%87.png)



### 求职资源

- [鱼皮的保姆级求职指南](https://www.codefather.cn/course/job)：从简历到面试的完整指南
- [编程导航的求职干货分享](https://www.codefather.cn/learn?category=76&type=2)：求职经验和技巧



## 持续学习资源

### 学习网站

- ⭐ [编程导航](https://www.codefather.cn/)：学习路线、项目教程、面试题
- [Linux 学习路线](https://www.codefather.cn/course/1789189862986850306/section/1990755996393320449)：完整的 Linux 学习路线

### 技术博客

- [Linux Journal](https://www.linuxjournal.com/)：Linux 和 Shell 技术
- [Red Hat Blog](https://www.redhat.com/en/blog)：Red Hat 系统管理
- [Ubuntu Blog](https://ubuntu.com/blog)：Ubuntu 官方博客

### Shell 脚本库

- [awesome-shell](https://github.com/alebcay/awesome-shell)：Shell 资源大全
- [pure-bash-bible](https://github.com/dylanaraps/pure-bash-bible)：Bash 技巧集合



## 总结

Shell 脚本是 Linux/Unix 系统管理和自动化运维的重要工具，虽然语法相对简单，但功能非常强大。通过编写 Shell 脚本，我们可以将重复性的工作自动化，大大提高工作效率。

学习 Shell 脚本不需要深入学习所有特性，掌握基础语法、流程控制、文本处理三剑客（grep、sed、awk）就足够应对大部分场景。重要的是要多写脚本、多实践，结合实际工作需求来学习。

在 AI 时代，虽然有了很多自动化工具，但 Shell 脚本仍然是最灵活、最高效的自动化方案。而且现在可以使用 AI 工具帮你生成和调试 Shell 脚本，学习起来更加轻松。

希望这份学习路线能够帮助大家快速掌握 Shell 脚本，提升工作效率。

加油小伙伴们 💪🏻！

![](https://pic.yupi.icu/1/%E5%8A%A0%E6%B2%B9%E8%A1%A8%E6%83%85%E5%8C%85.jpeg)




## 程序员必备资源

1）程序员学习交流圈：[极客教程、实战项目、求职宝典](https://www.codefather.cn)

2）程序员面试八股文：[实习/校招/社招高频考点、企业真题解析](https://www.mianshiya.com)

3）程序员写简历神器：[专业模板、丰富例句、直通面试](https://www.laoyujianli.com)

4）AI 知识资源大全：[前沿技术、最新 AI 资讯、提示词大全](https://ai.codefather.cn)

5）1 对 1 模拟面试：[实习/校招/社招面试拿 Offer 必备](https://ai.mianshiya.com)
