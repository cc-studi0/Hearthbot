import re

with open('BotMain/AI/CardEffectDB.cs', 'r', encoding='utf-8') as f:
    lines = f.readlines()

with open('generated_effects.txt', 'r', encoding='utf-8') as f:
    gen = f.read()

# 提取各段代码
spell_lines = []
bc_lines = []
dr_lines = []
eot_lines = []
current = None
for line in gen.split('\n'):
    if '新增法术效果' in line: current = spell_lines; continue
    elif '新增战吼效果' in line: current = bc_lines; continue
    elif '新增亡语效果' in line: current = dr_lines; continue
    elif '新增回合结束效果' in line: current = eot_lines; continue
    if current is not None and line.strip():
        # 去掉注释
        code = re.sub(r'\s*//.*$', '', line)
        if code.strip():
            current.append(code + '\n')

# 找到各方法的结束位置并插入
# RegisterSpells 结束于 "        }" 在 RegisterBattlecries 之前
# RegisterBattlecries 结束于 "        }" 在 RegisterDeathrattles 之前
# RegisterDeathrattles 结束于 "        }" 在 RegisterEndOfTurn 之前
# RegisterEndOfTurn 结束于 "        }" 在类结束之前

method_ends = []
for i, line in enumerate(lines):
    if 'private static void RegisterBattlecries' in line:
        # RegisterSpells 的 } 在此之前
        for j in range(i-1, 0, -1):
            if lines[j].strip() == '}':
                method_ends.append(('spell', j))
                break
    elif 'private static void RegisterDeathrattles' in line:
        for j in range(i-1, 0, -1):
            if lines[j].strip() == '}':
                method_ends.append(('bc', j))
                break
    elif 'private static void RegisterEndOfTurn' in line:
        for j in range(i-1, 0, -1):
            if lines[j].strip() == '}':
                method_ends.append(('dr', j))
                break

# RegisterEndOfTurn 的 } 是倒数第3行
for j in range(len(lines)-1, 0, -1):
    if lines[j].strip() == '}' and j < len(lines)-2:
        # 找到类结束前的方法结束
        for k in range(j-1, 0, -1):
            if lines[k].strip() == '}':
                method_ends.append(('eot', k))
                break
        break

# 按位置从后往前插入
inserts = {'spell': spell_lines, 'bc': bc_lines, 'dr': dr_lines, 'eot': eot_lines}
method_ends.sort(key=lambda x: x[1], reverse=True)

for name, pos in method_ends:
    code = inserts[name]
    if code:
        for c in reversed(code):
            lines.insert(pos, c)

# 同时修改 BuildDefault 添加新方法调用
for i, line in enumerate(lines):
    if 'RegisterEndOfTurn(db);' in line:
        lines.insert(i+1, '            RegisterHeroes(db);\n')
        lines.insert(i+2, '            RegisterLocations(db);\n')
        break

# 在文件末尾（类结束前）添加 RegisterHeroes 和 RegisterLocations 空方法
# 找到最后的 } }
for i in range(len(lines)-1, 0, -1):
    if lines[i].strip() == '}' and lines[i-1].strip() == '}':
        # 在倒数第二个 } 之前插入新方法
        lines.insert(i, '\n')
        lines.insert(i+1, '        private static void RegisterHeroes(CardEffectDB db)\n')
        lines.insert(i+2, '        {\n')
        lines.insert(i+3, '        }\n')
        lines.insert(i+4, '\n')
        lines.insert(i+5, '        private static void RegisterLocations(CardEffectDB db)\n')
        lines.insert(i+6, '        {\n')
        lines.insert(i+7, '        }\n')
        break

with open('BotMain/AI/CardEffectDB.cs', 'w', encoding='utf-8') as f:
    f.writelines(lines)

print(f'插入法术: {len(spell_lines)} 条')
print(f'插入战吼: {len(bc_lines)} 条')
print(f'插入亡语: {len(dr_lines)} 条')
print(f'插入回合结束: {len(eot_lines)} 条')
print('已更新 CardEffectDB.cs')
