// Explicit runtime qualification only. The fixed supervisor owns the deadline and cleanup, even if this fails.
internal static class SandboxTaskLimitFixture
{
    public const string Script = """
        import assert from 'node:assert/strict';
        import fs from 'node:fs';
        import {spawn, spawnSync} from 'node:child_process';
        import {Worker} from 'node:worker_threads';
        let stage = 'identity';
        const accounting = {pid:process.pid, baseline:null, exhausted:null, children:[]};
        function taskSnapshot() {
          const ids = fs.readdirSync('/proc').filter(id => /^\d+$/.test(id));
          assert(ids.length <= 48, 'Unexpected process inventory size');
          return ids.map(id => {
            const info = fs.readFileSync('/proc/'+id+'/status','utf8');
            const field = name => {
              const match = info.match(new RegExp('^'+name+':\\s+(\\d+)', 'm'));
              assert(match, 'Missing process field: '+name);
              return Number(match[1]);
            };
            return {pid:Number(id),ppid:field('PPid'),uid:field('Uid'),threads:field('Threads')};
          });
        }
        try {
        assert.equal(process.getuid(), 65534);
        assert.equal(process.getgid(), 65534);
        const status = fs.readFileSync('/proc/self/status','utf8');
        stage = 'effective-capabilities';
        assert.match(status, /^CapEff:\s+0+$/m);
        stage = 'regain-root';
        assert.throws(() => process.setuid(0), e => e.code === 'EPERM');
        const limits = fs.readFileSync('/proc/self/limits','utf8');
        stage = 'visible-process-limits';
        assert.match(limits, /^Max processes\s+31\s+31\s+processes\s*$/m);
        stage = 'raise-hard-limit';
        const raise = spawnSync('/bin/sh',['-c','ulimit -H -u 32'],{encoding:'utf8',timeout:2000});
        assert.equal(raise.error, undefined);
        assert.notEqual(raise.status, 0);
        assert.match(raise.stderr, /not permitted/i);
        const children = [];
        let worker, processError = '', threadError = '', workloadTasks = 0;
        try {
          stage = 'baseline-task-accounting';
          accounting.baseline = taskSnapshot();
          for (let attempt = 0; attempt < 40; attempt++) {
            stage = 'sleep-child-' + attempt;
            const child = spawn('/bin/sleep',['30'],{stdio:'ignore'});
            const result = await new Promise(resolve => {
              child.once('spawn',() => resolve('spawned'));
              child.once('error',e => resolve(e.code));
            });
            if (result !== 'spawned') { processError = result; break; }
            children.push(child);
            accounting.children.push(child.pid);
          }
          stage = 'process-limit-error';
          assert.equal(processError, 'EAGAIN');
          assert(children.length > 0 && children.length < 40);
          stage = 'count-workload-tasks';
          accounting.exhausted = taskSnapshot();
          workloadTasks = accounting.exhausted.filter(row => row.uid === 65534).reduce((sum,row) => sum + row.threads, 0);
          assert.equal(workloadTasks, 32);
          stage = 'worker-thread-limit';
          try {
            worker = new Worker('setInterval(() => {}, 1000)',{eval:true});
            await new Promise((resolve,reject) => {
              const timer = setTimeout(() => reject(Error('Worker result unavailable')),2000);
              worker.once('online',() => {clearTimeout(timer);resolve();});
              worker.once('error',e => {clearTimeout(timer);reject(e);});
            });
          } catch (e) { threadError = String(e.message); }
          assert.match(threadError, /EAGAIN|resource temporarily unavailable/i);
        } finally {
          if (worker) await worker.terminate();
          await Promise.all(children.map(child => new Promise(resolve => {
            if (child.exitCode !== null || child.signalCode !== null) { resolve(); return; }
            child.once('exit',resolve);child.kill('SIGKILL');
          })));
        }
        stage = 'save-proof';
        fs.writeFileSync('result.txt',JSON.stringify({uid:process.getuid(),workloadTasks,processError,threadRefused:true,childrenReaped:children.length,accounting}));
        } catch (error) {
          // Count every guest task; an extra guest cannot escape the bill by changing its dinner jacket.
          fs.writeFileSync('result.txt',JSON.stringify({failed:true,stage,errorCode:error.code??null,errorName:error.name,errorMessage:String(error.message).slice(0,1000),accounting}));
          throw error;
        }
        """;
}
