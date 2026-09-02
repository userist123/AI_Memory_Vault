package com.nalo.medquery.domain.model.appointment;

import java.time.LocalDateTime;

import com.nalo.medquery.domain.entity.AppointmentEntity;
import com.nalo.medquery.domain.model.appointment.DetailedAppointmentDataDTO;


import jakarta.validation.constraints.Future;
import jakarta.validation.constraints.NotNull;

public record DetailedAppointmentDataDTO(
    Long id,
    Long doctorId,
    @NotNull    
    Long patientId,
    @NotNull 
    @Future   
    LocalDateTime date
) {

    public DetailedAppointmentDataDTO(AppointmentEntity appointment) {
        this(
            appointment.getId(),
            appointment.getDoctor().getId(),
            appointment.getPatient().getId(),
            appointment.getDate()
        );
    }
}
