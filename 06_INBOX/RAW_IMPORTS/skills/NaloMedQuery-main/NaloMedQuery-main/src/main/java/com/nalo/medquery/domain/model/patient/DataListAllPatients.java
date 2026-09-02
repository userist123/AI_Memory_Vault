package com.nalo.medquery.domain.model.patient;

import com.nalo.medquery.domain.entity.PatientEntity;

public record DataListAllPatients(
    Long id,
    String nome,
    String email,
    String telefone    
) {
    public DataListAllPatients(PatientEntity entity){
        this(entity.getId(), entity.getNome(), entity.getEmail(), entity.getTelefone());
    }
}
