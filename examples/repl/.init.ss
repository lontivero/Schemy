; `.init.ss` is picked up by interpreter automatically
(define square (lambda (x) (* x x)))
(define author "Lucas")
(define-macro . (lambda (m i)(define mm (symbol->string `,m)) `(__invoke_getter ,mm ,i)))