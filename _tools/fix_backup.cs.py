import sys
path = sys.argv[1]

with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

old_switch = '''            ".mp4" or ".mov" or ".mkv" => "video/mp4",

            ".png" => "image/png",

            ".gif" => "image/gif",

            ".webp" => "image/webp",

            _ => "image/jpeg"'''

new_switch = '''            ".mp4" or ".mov" or ".mkv" or ".avi" or ".wmv" or ".webm" or ".3gp" => "video/mp4",

            ".png" => "image/png",

            ".gif" => "image/gif",

            ".webp" => "image/webp",

            ".heic" => "image/heic",

            ".heif" => "image/heif",

            ".bmp" => "image/bmp",

            _ => "image/jpeg"'''

if old_switch not in content:
    print('Pattern not found in file')
    sys.exit(1)

content = content.replace(old_switch, new_switch)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

print('Done')
