import os
import re
import json

cwd = os.getcwd()
out_dir = os.path.join(cwd, 'output')
os.makedirs(out_dir, exist_ok=True)

# 遍历当前目录下的所有文件夹，这里的 root_id 就是第一层文件夹名（对应枚举ID，即 x2 里的 2）
for root_id in os.listdir(cwd):
    root_path = os.path.join(cwd, root_id)
    if not os.path.isdir(root_path) or root_id == 'output':
        continue

    # 遍历第二层文件夹，这里的 ver_folder 名字里包含的数字会被提取（即 300）
    for ver_folder in os.listdir(root_path):
        ver_path = os.path.join(root_path, ver_folder)
        info_path = os.path.join(ver_path, 'info.json')
        if not os.path.isfile(info_path):
            continue

        with open(info_path, 'r', encoding='utf-8') as f:
            data = json.load(f)

        # 从 info.json 提取 Unity 版本号 (例如 2019.4.15f1 -> major=2019, minor=4)
        orig = data.get('Version', '')
        m = re.match(r'^(\d+)\.(\d+)', orig)
        if m:
            major, minor = m.groups()
        else:
            parts = re.findall(r'\d+', orig)
            major, minor = parts[0], parts[1] if len(parts) > 1 else ('', '')

        # 核心逻辑：提取第二层文件夹名里的所有数字
        digits = ''.join(filter(str.isdigit, ver_folder))
        
        # 拼接逻辑：[Unity主版本].[次版本].[游戏版本号去除.]x[第一层文件夹名]
        # 结果示例：2019.4.300x2 代表SR 3.0.0
        new_version = f"{major}.{minor}.{digits}x{root_id}"
        data['Version'] = new_version

        out_file = os.path.join(out_dir, f"{new_version}.json")
        with open(out_file, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, separators=(',',':'))

        print(f"[OK] {info_path} -> {out_file}")