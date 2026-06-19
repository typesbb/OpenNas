import sys

path = sys.argv[1]
with open(path, 'r', encoding='utf-8') as f:
    lines = f.readlines()

found = False
for i, line in enumerate(lines):
    stripped = line.rstrip("\n").rstrip("\r")
    if stripped.strip() == '".mp4" or ".mov" or ".mkv" => "video/mp4",':
        lines[i] = line.replace(
            '".mp4" or ".mov" or ".mkv"',
            '".mp4" or ".mov" or ".mkv" or ".avi" or ".wmv" or ".webm" or ".3gp"')
        found = True
    elif found and stripped.strip() == '".webp" => "image/webp",':
        insert_idx = i + 1
        indent = line[:len(line) - len(line.lstrip())]
        lines.insert(insert_idx, f'{indent}".heic" => "image/heic",\n')
        lines.insert(insert_idx + 1, f'{indent}".heif" => "image/heif",\n')
        lines.insert(insert_idx + 2, f'{indent}".bmp" => "image/bmp",\n')
        found = False
        break

with open(path, 'w', encoding='utf-8') as f:
    f.writelines(lines)
print("Done")
