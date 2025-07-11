(define-macro let
              (lambda args
                (define specs (car args)) ; ( (var1 val1), ... )
                (define bodies (cdr args)) ; (expr1 ...)
                (if (null? specs)
                  `((lambda () ,@bodies))
                  (begin
                    (define spec1 (car specs)) ; (var1 val1)
                    (define spec_rest (cdr specs)) ; ((var2 val2) ...)
                    (define inner `((lambda ,(list (car spec1)) ,@bodies) ,(car (cdr spec1))))
                    `(let ,spec_rest ,inner)))))

(define-macro cond
              (lambda args
                (if (= 0 (length args)) ''()
                  (begin
                    (define first (car args))
                    (define rest (cdr args))
                    (define test1 (if (equal? (car first) 'else) '#t (car first)))
                    (define expr1 (car (cdr first)))
                    `(if ,test1 ,expr1 (cond ,@rest))))))
                    
(define not? (lambda (x) (if x #f #t)))
(define xor (lambda (a b) (and (or a b) (not? (and a b)))))

(define nil '())

; fold function (f) over list (xs) while accumulating (a)
(define fold (lambda (f a xs)
  (if (= 0 (length xs)) a
      (fold f (f (car xs) a) (cdr xs)))
  ))

(define reverse (lambda (xs) (fold cons nil xs)))

(define sum (lambda (xs) (fold + 0 xs)))
(define dec (lambda (x) (- x 1)))
(define inc (lambda (x) (+ 1 x)))

(define odd? (lambda (x) (= 1 (% x 2))))
(define even? (lambda (x) (not? (odd? x))))

(define require (lambda (e) (if e e (amb))))

(define member? (lambda (item lst)
     (if (< 0 (length lst))
         (if (= item (car lst))
             #t
             (member? item (cdr lst)))
         #f)))

(define distinct? (lambda (lst)
    (if (< 0 (length lst))
         (if (member? (car lst) (cdr lst))
             #f
             (distinct? (cdr lst)))
         #t)))

(define exclude (lambda (items lst)
     (if (< 0 (length lst))
         (if (member? (car lst) items)
             (exclude items (cdr lst))
             (cons (car lst) (exclude items (cdr lst))))
         '())))

(define cadr (lambda (xs) (car (cdr xs))))
(define caddr (lambda (xs) (cadr (cdr xs))))
(define cadddr (lambda (xs) (caddr (cdr xs))))
(define caddddr (lambda (xs) (cadddr (cdr xs))))
(define cadddddr (lambda (xs) (caddddr (cdr xs))))
(define caddddddr (lambda (xs) (cadddddr (cdr xs))))

(define fst car)
(define snd cadr)
(define trd caddr)
