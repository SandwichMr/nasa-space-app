"""Independent reference model (D5). Lossless battery, constant powers, essential before science,
battery covers deficit up to max discharge while E>0, absorbs surplus up to max charge while E<cap.
Event-driven: integrates exactly between dispatch changes. Written from the contract text, not from PowerSystem."""
from fractions import Fraction as F

def dispatch(c, E, sci_on):
    gen, ess, sci = c['gen'], c['ess'], (c['sci'] if sci_on else F(0))
    dis = c['dis'] if E > 0 else F(0)
    chg = c['chg'] if E < c['cap'] else F(0)
    avail = gen + dis
    ess_s = min(ess, avail)
    sci_s = min(sci, avail - ess_s)
    net = gen - ess_s - sci_s
    bat = min(net, chg) if net > 0 else net
    return ess_s, sci, sci_s, bat

def run(c, rules, script):
    """script: list of ('resume'|'pause'|'science',val|'tick',dt|'restart'). Mission per contract/D2 rules:
    work accrues only while science fully served; ends: essential lost > objective > duration."""
    st = dict(E=c['E0'], t=F(0), work=F(0), sci=False, state='Ready', epoch=0, rev=0)
    log = []
    def snap(label):
        ess_s, sreq, ssrv, _ = dispatch(c, st['E'], st['sci_req'])
        log.append((label, st['epoch'], st['rev'], st['state'], float(st['t']), float(st['E']), float(st['work']),
                    st['sci'], float(ess_s), float(sreq), float(ssrv)))
    st['sci_req'] = False
    snap('init')
    for op in script:
        kind = op[0]
        if kind == 'resume' and st['state'] in ('Ready', 'Paused'): st['state'] = 'Running'; st['rev'] += 1; snap(op)
        elif kind == 'pause' and st['state'] == 'Running': st['state'] = 'Paused'; st['rev'] += 1; snap(op)
        elif kind == 'science' and st['state'] in ('Ready', 'Running', 'Paused'): st['sci'] = op[1]; st['rev'] += 1; snap(op)
        elif kind == 'restart':
            st.update(E=c['E0'], t=F(0), work=F(0), sci=False, state='Ready', epoch=st['epoch'] + 1, rev=0, sci_req=False); snap(op)
        elif kind == 'tick':
            if st['state'] != 'Running': continue
            remaining = F(op[1]); outcome = None; progressed = False
            st['sci_req'] = st['sci']
            while remaining > 0 and outcome is None:
                ess_s, sreq, ssrv, bat = dispatch(c, st['E'], st['sci'])
                if ess_s < c['ess']: outcome = 'EssentialServiceLost'; progressed = True; break
                # time to next event: battery boundary, objective, duration
                tb = (c['cap'] - st['E']) / bat if bat > 0 else (st['E'] / -bat if bat < 0 else None)
                full_sci = st['sci'] and sreq > 0 and ssrv >= sreq
                to_obj = rules['target'] - st['work'] if full_sci else None
                to_dur = rules['duration'] - st['t']
                step = min(x for x in [remaining, tb, to_obj, to_dur] if x is not None)
                st['E'] += bat * step
                if st['E'] < 0 or st['E'] > c['cap']: raise RuntimeError('reference left bounds')
                st['t'] += step; remaining -= step; progressed = True
                if full_sci: st['work'] += step
                ess_s2, _, _, _ = dispatch(c, st['E'], st['sci'])
                if ess_s2 < c['ess']: outcome = 'EssentialServiceLost'
                elif st['work'] >= rules['target']: outcome = 'ObjectiveAchieved'
                elif st['t'] >= rules['duration']: outcome = 'TimeExpired'
            if progressed:
                st['rev'] += 1
                if outcome in ('ObjectiveAchieved', 'TimeExpired'): st['state'] = 'Completed'
                elif outcome == 'EssentialServiceLost': st['state'] = 'Interrupted'
                st['outcome'] = outcome
                snap(op + (outcome,))
    return log

def cfg(E0, cap, gen, ess, sci, chg, dis):
    return dict(E0=F(E0), cap=F(cap), gen=F(gen), ess=F(ess), sci=F(sci), chg=F(chg), dis=F(dis))

FIXTURES = {
 'S1_BalanceObjective': (cfg(18000, 36000, 50, 60, 20, 40, 40), dict(target=F(60), duration=F(600)),
     [('resume',), ('tick', 60), ('science', True), ('tick', 30), ('pause',), ('tick', 30), ('resume',), ('tick', 40), ('restart',)]),
 'S2_DepletionEssentialLost': (cfg(600, 1000, 50, 60, 20, 40, 40), dict(target=F(100), duration=F(600)),
     [('science', True), ('resume',), ('tick', 50)]),
 'S3_PartialScienceTimeExpired': (cfg(0, 1000, 70, 60, 20, 40, 40), dict(target=F(10), duration=F(30)),
     [('science', True), ('resume',), ('tick', 45)]),
 'S4_ChargeToFullCurtailment': (cfg(900, 1000, 100, 60, 20, 30, 40), dict(target=F(100), duration=F(600)),
     [('resume',), ('tick', 10), ('science', True), ('tick', 10)]),
}
if __name__ == '__main__':
    for name, (c, r, s) in FIXTURES.items():
        print('==', name)
        for row in run(c, r, s): print('  ', row)
