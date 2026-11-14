"""Static audit: every guid referenced by .prefab/.unity/.mat/.asset under Assets/_Project must exist as a .meta
guid in the same tree, or be a Unity built-in/package guid (reported separately). Reads a git tree via `git show`."""
import re, subprocess, sys
ref = sys.argv[1]
files = subprocess.run(['git','ls-tree','-r','--name-only',ref], capture_output=True, text=True).stdout.split('\n')
guids = {}
for f in files:
    if f.endswith('.meta'):
        m = re.search(r'^guid: ([0-9a-f]{32})', subprocess.run(['git','show',f'{ref}:{f}'],capture_output=True,text=True).stdout, re.M)
        if m: guids[m.group(1)] = f[:-5]
builtin = {'0000000000000000e000000000000000','0000000000000000f000000000000000','0000000000000000f000000000000000'}
problems = 0; checked = 0
for f in files:
    if f.startswith('Assets/_Project/') and f.endswith(('.prefab','.unity','.mat','.asset')):
        text = subprocess.run(['git','show',f'{ref}:{f}'],capture_output=True,text=True).stdout
        for g in sorted(set(re.findall(r'guid: ([0-9a-f]{32})', text))):
            checked += 1
            if g in guids: print(f'  ok   {f} -> {guids[g]}')
            elif g in builtin: print(f'  ok   {f} -> builtin {g}')
            else: problems += 1; print(f'  MISS {f} -> {g} (not in repo .meta; may be package/builtin)')
        if re.search(r'm_Script: \{fileID: 0\}', text): problems += 1; print(f'  MISS {f}: missing script (fileID 0)')
print(f'{ref}: {checked} guid refs, {problems} unresolved')
